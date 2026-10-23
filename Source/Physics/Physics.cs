using Box2D.NetStandard.Collision;
using Box2D.NetStandard.Collision.Shapes;
using Box2D.NetStandard.Dynamics.Bodies;
using Box2D.NetStandard.Dynamics.Contacts;
using Box2D.NetStandard.Dynamics.Fixtures;
using Box2D.NetStandard.Dynamics.Joints;
using Box2D.NetStandard.Dynamics.World;
using Box2D.NetStandard.Dynamics.World.Callbacks;
using System.Collections.Concurrent;
using System.Numerics;

namespace Atlas2D.PhysicsBox2D
{
    /// <summary>
    /// The physics world. It steps on its own thread, so:
    /// <list type="bullet">
    /// <item>Setters (forces, velocity, position, ...) are queued and applied at the start of the next step.</item>
    /// <item>Getters return the body's state from the end of the last step.</item>
    /// <item>Creating and removing bodies or joints happens right away (it waits for the current step to finish).</item>
    /// </list>
    /// </summary>
    public static class Physics
    {
        private enum CommandType
        {
            CreateBody,
            RemoveBody,
            ApplyForce,
            ApplyImpulse,
            ApplyTorque,
            SetAngle,
            SetAngularVelocity,
            SetVelocity,
            SetPosition,
            SetBodyFixedRotation,
            SetFilterData,
            CreateJoint,
            RemoveJoint,
        }

        private struct PhysicsCommand
        {
            public CommandType Type;
            public uint ID;
            public Vector2 Vector;
            public Vector2 Point;
            public float Value;
            public bool Flag;
            public Filter Filter;
            public BodyDef BodyDef;
            public FixtureDef FixtureDef;
            public MassData MassData;
            public JointDef JointDef;
        }

        private struct BodyState
        {
            public Vector2 Position;
            public float Rotation;
            public Vector2 Velocity;
            public float AngularVelocity;
        }

        // Scaling the physics so that 50 pixels equates to 1 meter in the simulation
        public static readonly float WorldScalar = 50f;

        /// <summary>
        /// Lock this while using Box2D objects directly (<see cref="GetWorld"/>, <see cref="GetBody"/>,
        /// <see cref="GetJoint"/>), so the physics thread can't step at the same time.
        /// </summary>
        public static object WorldLock { get; } = new object();

        private static readonly ConcurrentQueue<PhysicsCommand> _commands = new();
        private static readonly Dictionary<uint, Body> _bodies = new();
        private static readonly Dictionary<uint, Joint> _joints = new();
        private static uint _nextBodyID = 0;
        private static uint _nextJointID = 0;
        private static World world;

        // Copy of each body's state after the last step, so the main thread can read it without waiting
        private static readonly object _stateLock = new object();
        private static readonly Dictionary<uint, BodyState> _bodyStates = new();

        /// <summary>
        /// This is called while the game is loading. Initializes the main physics world
        /// (Other physics worlds can be created separately from this using the Box2DX classes).
        /// </summary>
        public static void Initialize(Vector2 _gravity, bool _doSleep)
        {
            lock (WorldLock)
            {
                world = new World(_gravity);
                world.SetAllowSleeping(_doSleep);
                world.SetContactFilter(new ContactFilter());
                world.SetContactListener(new WorldContactListener());
                world.SetDestructionListener(new WorldDestructionListener());
            }
        }

        /// <summary>
        /// Gets the physics world. Lock <see cref="WorldLock"/> while using it.
        /// </summary>
        public static World GetWorld()
        {
            return world;
        }

        /// <summary>
        /// Steps the physics simulation. Called from the physics thread.
        /// </summary>
        internal static void Step(float _deltaTime, int _velocityIterations, int _positionIterations)
        {
            lock (WorldLock)
            {
                ApplyCommands();
                world.Step(_deltaTime, _velocityIterations, _positionIterations);
                // Runs anything queued during the step, like bodies created from collision callbacks
                ApplyCommands();
                SaveBodyStates();
            }
        }

        private static void ApplyCommands()
        {
            while (_commands.TryDequeue(out PhysicsCommand command))
            {
                ApplyCommand(command);
            }
        }

        private static void ApplyCommand(in PhysicsCommand command)
        {
            switch (command.Type)
            {
                case CommandType.CreateBody:
                    CreateBodyNow(command.ID, command.BodyDef, command.FixtureDef, command.MassData, command.Flag);
                    return;
                case CommandType.RemoveBody:
                    RemoveBodyNow(command.ID);
                    return;
                case CommandType.CreateJoint:
                    CreateJointNow(command.ID, command.JointDef);
                    return;
                case CommandType.RemoveJoint:
                    RemoveJointNow(command.ID);
                    return;
            }

            // The body may have been removed after the command was queued
            if (!_bodies.TryGetValue(command.ID, out Body body)) return;

            switch (command.Type)
            {
                case CommandType.ApplyForce:
                    body.ApplyForce(command.Vector, command.Point);
                    break;
                case CommandType.ApplyImpulse:
                    body.ApplyLinearImpulse(command.Vector, command.Point);
                    break;
                case CommandType.ApplyTorque:
                    body.ApplyTorque(command.Value);
                    break;
                case CommandType.SetAngle:
                    body.SetTransform(body.GetPosition(), command.Value * (MathF.PI / 180));
                    break;
                case CommandType.SetAngularVelocity:
                    body.SetAngularVelocity(command.Value);
                    break;
                case CommandType.SetVelocity:
                    body.SetLinearVelocity(command.Vector);
                    break;
                case CommandType.SetPosition:
                    body.SetTransform(command.Vector / WorldScalar, body.GetAngle());
                    break;
                case CommandType.SetBodyFixedRotation:
                    body.SetFixedRotation(command.Flag);
                    break;
                case CommandType.SetFilterData:
                    for (Fixture f = body.GetFixtureList(); f != null; f = f.Next)
                    {
                        f.FilterData = command.Filter;
                        f.Refilter();
                    }
                    break;
            }
        }

        private static void SaveBodyStates()
        {
            lock (_stateLock)
            {
                foreach (var pair in _bodies)
                {
                    _bodyStates[pair.Key] = ReadBodyState(pair.Value);
                }
            }
        }

        private static BodyState ReadBodyState(Body body)
        {
            return new BodyState()
            {
                Position = body.GetPosition() * WorldScalar,
                Rotation = 180 / MathF.PI * body.GetAngle(),
                Velocity = body.GetLinearVelocity(),
                AngularVelocity = body.GetAngularVelocity()
            };
        }

        #region Joints
        /// <summary>
        /// Creates a joint from a joint definition.
        /// </summary>
        public static uint CreateJoint(JointDef _jointDef)
        {
            lock (WorldLock)
            {
                uint id = _nextJointID++;
                if (world.IsLocked())
                {
                    _commands.Enqueue(new PhysicsCommand() { Type = CommandType.CreateJoint, ID = id, JointDef = _jointDef });
                }
                else
                {
                    CreateJointNow(id, _jointDef);
                }
                return id;
            }
        }

        private static void CreateJointNow(uint _id, JointDef _jointDef)
        {
            Joint joint = world.CreateJoint(_jointDef);
            joint.UserData = _id;
            _joints.Add(_id, joint);
        }

        /// <summary>
        /// Removes a joint.
        /// </summary>
        public static void RemoveJoint(uint _id)
        {
            lock (WorldLock)
            {
                if (world.IsLocked())
                {
                    _commands.Enqueue(new PhysicsCommand() { Type = CommandType.RemoveJoint, ID = _id });
                }
                else
                {
                    RemoveJointNow(_id);
                }
            }
        }

        private static void RemoveJointNow(uint _id)
        {
            if (!_joints.Remove(_id, out Joint joint))
            {
                Debug.LogError($"PHYSICS: Joint with ID: [ {_id} ] does not exist, cannot remove.");
                return;
            }
            world.DestroyJoint(joint);
        }

        /// <summary>
        /// Returns the joint with the given ID, or null if it does not exist. Lock <see cref="WorldLock"/> while using it.
        /// </summary>
        public static Joint GetJoint(uint _id)
        {
            lock (WorldLock)
            {
                return _joints.TryGetValue(_id, out Joint joint) ? joint : null;
            }
        }
        #endregion

        #region Bodies
        /// <summary>
        /// Creates a physics body and returns its ID.
        /// </summary>
        /// <param name="_bodyDefinition">The definition of the physics body.</param>
        /// <param name="_fixtureDef">The definition of the fixture. This holds the parameters of the physics body.</param>
        /// <param name="_massData">The mass data of the physics body.</param>
        public static uint CreatePhysicsBody(BodyDef _bodyDefinition, FixtureDef _fixtureDef, MassData _massData, bool _isKinematic)
        {
            lock (WorldLock)
            {
                uint id = _nextBodyID++;
                if (world.IsLocked())
                {
                    _commands.Enqueue(new PhysicsCommand()
                    {
                        Type = CommandType.CreateBody,
                        ID = id,
                        BodyDef = _bodyDefinition,
                        FixtureDef = _fixtureDef,
                        MassData = _massData,
                        Flag = _isKinematic
                    });
                    return id;
                }

                return CreateBodyNow(id, _bodyDefinition, _fixtureDef, _massData, _isKinematic) ? id : uint.MaxValue;
            }
        }

        private static bool CreateBodyNow(uint _id, BodyDef _bodyDefinition, FixtureDef _fixtureDef, MassData _massData, bool _isKinematic)
        {
            try
            {
                Body b = world.CreateBody(_bodyDefinition);
                b.SetUserData(_id);
                b.CreateFixture(_fixtureDef);
                b.SetMassData(_massData);
                b.SetType(_isKinematic ? BodyType.Kinematic : BodyType.Dynamic);
                _bodies.Add(_id, b);

                lock (_stateLock)
                {
                    _bodyStates[_id] = ReadBodyState(b);
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"PHYSICS: Could not create physics body [{_id}]");
                Debug.LogError(e.Message);
                return false;
            }
        }

        /// <summary>
        /// Removes a physics body from the simulation.
        /// </summary>
        public static void RemovePhysicsBody(uint _id)
        {
            lock (WorldLock)
            {
                if (world.IsLocked())
                {
                    _commands.Enqueue(new PhysicsCommand() { Type = CommandType.RemoveBody, ID = _id });
                }
                else
                {
                    RemoveBodyNow(_id);
                }
            }
        }

        private static void RemoveBodyNow(uint _id)
        {
            // Removed from the dictionary first, so a second remove call during DestroyBody does nothing
            if (!_bodies.Remove(_id, out Body body)) return;
            lock (_stateLock)
            {
                _bodyStates.Remove(_id);
            }

            // Box2D also destroys the body's joints
            for (JointEdge edge = body.GetJointList(); edge != null; edge = edge.next)
            {
                if (edge.joint.UserData is uint jointID)
                {
                    _joints.Remove(jointID);
                }
            }

            world.DestroyBody(body);
        }

        /// <summary>
        /// Gets the physics body associated with the given ID. Lock <see cref="WorldLock"/> while using it.
        /// </summary>
        public static Body GetBody(uint _id)
        {
            lock (WorldLock)
            {
                return _bodies.TryGetValue(_id, out Body body) ? body : null;
            }
        }
        #endregion

        #region Getters
        /// <summary>
        /// Gets the position of a physics body after the last step.
        /// </summary>
        public static Vector2 GetBodyPosition(uint _id)
        {
            lock (_stateLock)
            {
                return _bodyStates.TryGetValue(_id, out BodyState state) ? state.Position : Vector2.Zero;
            }
        }

        /// <summary>
        /// Gets the rotation (in degrees) of a physics body after the last step.
        /// </summary>
        public static float GetBodyRotation(uint _id)
        {
            lock (_stateLock)
            {
                return _bodyStates.TryGetValue(_id, out BodyState state) ? state.Rotation : 0;
            }
        }

        /// <summary>
        /// Gets the linear velocity of a physics body after the last step.
        /// </summary>
        public static Vector2 GetBodyVelocity(uint _id)
        {
            lock (_stateLock)
            {
                return _bodyStates.TryGetValue(_id, out BodyState state) ? state.Velocity : Vector2.Zero;
            }
        }

        /// <summary>
        /// Gets the angular velocity of a physics body after the last step.
        /// </summary>
        public static float GetBodyAngularVelocity(uint _id)
        {
            lock (_stateLock)
            {
                return _bodyStates.TryGetValue(_id, out BodyState state) ? state.AngularVelocity : 0;
            }
        }

        /// <summary>
        /// Gets the filter data from the fixtures on a physics body.
        /// </summary>
        public static Filter[] GetFilterData(uint _id)
        {
            lock (WorldLock)
            {
                if (!_bodies.TryGetValue(_id, out Body body)) return null;

                List<Filter> filters = new List<Filter>();
                for (Fixture f = body.GetFixtureList(); f != null; f = f.Next)
                {
                    filters.Add(f.FilterData);
                }
                return filters.ToArray();
            }
        }
        #endregion

        #region Setters
        /// <summary>
        /// Applies a force to a physics body on the next step.
        /// </summary>
        /// <param name="_point">The point the force is applied to.</param>
        public static void ApplyForce(uint _id, Vector2 _force, Vector2 _point)
        {
            _commands.Enqueue(new PhysicsCommand() { Type = CommandType.ApplyForce, ID = _id, Vector = _force, Point = _point });
        }

        /// <summary>
        /// Applies an impulse to a physics body on the next step.
        /// </summary>
        /// <param name="_point">The point the impulse is applied to.</param>
        public static void ApplyImpulse(uint _id, Vector2 _impulse, Vector2 _point)
        {
            _commands.Enqueue(new PhysicsCommand() { Type = CommandType.ApplyImpulse, ID = _id, Vector = _impulse, Point = _point });
        }

        /// <summary>
        /// Applies a torque to a physics body on the next step.
        /// </summary>
        public static void ApplyTorque(uint _id, float _torque)
        {
            _commands.Enqueue(new PhysicsCommand() { Type = CommandType.ApplyTorque, ID = _id, Value = _torque });
        }

        /// <summary>
        /// Sets the angle (in degrees) of a physics body on the next step.
        /// </summary>
        public static void SetAngle(uint _id, float _angle)
        {
            _commands.Enqueue(new PhysicsCommand() { Type = CommandType.SetAngle, ID = _id, Value = _angle });
        }

        /// <summary>
        /// Sets the angular velocity of a physics body on the next step.
        /// </summary>
        public static void SetAngularVelocity(uint _id, float _angularVelocity)
        {
            _commands.Enqueue(new PhysicsCommand() { Type = CommandType.SetAngularVelocity, ID = _id, Value = _angularVelocity });
        }

        /// <summary>
        /// Sets the linear velocity of a physics body on the next step.
        /// </summary>
        public static void SetVelocity(uint _id, Vector2 _linearVelocity)
        {
            _commands.Enqueue(new PhysicsCommand() { Type = CommandType.SetVelocity, ID = _id, Vector = _linearVelocity });
        }

        /// <summary>
        /// Sets the position of a physics body on the next step. Use this instead of setting the node's position
        /// so that movements are handled within the physics engine.
        /// </summary>
        public static void SetPosition(uint _id, Vector2 _position)
        {
            _commands.Enqueue(new PhysicsCommand() { Type = CommandType.SetPosition, ID = _id, Vector = _position });
        }

        /// <summary>
        /// Toggles the fixed rotation of a physics body on the next step.
        /// </summary>
        public static void SetBodyFixedRotation(uint _id, bool _fixedRotation)
        {
            _commands.Enqueue(new PhysicsCommand() { Type = CommandType.SetBodyFixedRotation, ID = _id, Flag = _fixedRotation });
        }

        /// <summary>
        /// Sets the filter data of the fixtures on a physics body on the next step.
        /// </summary>
        public static void SetFilterData(uint _id, Filter _filter)
        {
            _commands.Enqueue(new PhysicsCommand() { Type = CommandType.SetFilterData, ID = _id, Filter = _filter });
        }
        #endregion

        /// <summary>
        /// Casts a ray into the physics world, and returns the hit data.
        /// </summary>
        /// <param name="_point">The starting point of the ray.</param>
        /// <param name="_direction">The direction of the ray.</param>
        /// <param name="_maxDistance">The maximum distance the ray can travel.</param>
        public static bool Raycast(Vector2 _point, Vector2 _direction, float _maxDistance, out RaycastHit hit)
        {
            hit = new RaycastHit();
            hit.MaxDistance = _maxDistance;
            try
            {
                lock (WorldLock)
                {
                    world.RayCast(hit.Callback, _point, _point + (_direction * _maxDistance));
                }
            }
            catch (Exception e)
            {
                return false;
            }
            return hit.Hit;
        }

        private static uint GetIDFromBody(Body _body)
        {
            return _body.GetUserData<uint>();
        }

        public class WorldContactListener : ContactListener
        {
            public void BeginContact(Contact contact)
            {
                Fixture fixtureA = contact.FixtureA;
                Fixture fixtureB = contact.FixtureB;

                Body bodyA = fixtureA.Body;
                Body bodyB = fixtureB.Body;

                if(bodyA == null || bodyB == null)
                {
                    Debug.LogError("PHYSICS: Contact body is null, cannot invoke collision.");
                    return;
                }

                uint idA = GetIDFromBody(bodyA);
                uint idB = GetIDFromBody(bodyB);

                if (fixtureA.IsSensor())
                {
                    RigidbodySensorNode.InvokeCollision(idA, idB, true);
                }
                else if (fixtureB.IsSensor())
                {
                    RigidbodySensorNode.InvokeCollision(idB, idA, true);
                }
                else
                {
                    // Both are rigidbodies
                    RigidbodyNode.InvokeCollision(idA, idB, true);
                    RigidbodyNode.InvokeCollision(idB, idA, true);
                }
            }

            public override void BeginContact(in Contact contact)
            {
                
            }

            public void EndContact(Contact contact)
            {
                Fixture fixtureA = contact.FixtureA;
                Fixture fixtureB = contact.FixtureB;

                Body bodyA = fixtureA.Body;
                Body bodyB = fixtureB.Body;

                if (bodyA == null || bodyB == null)
                {
                    Debug.LogError("PHYSICS: Contact body is null, cannot invoke collision.");
                    return;
                }

                uint idA = GetIDFromBody(bodyA);
                uint idB = GetIDFromBody(bodyB);

                if (fixtureA.IsSensor())
                {
                    RigidbodySensorNode.InvokeCollision(idA, idB, false);
                }
                else if (fixtureB.IsSensor())
                {
                    RigidbodySensorNode.InvokeCollision(idB, idA, false);
                }
                else
                {
                    // Both are rigidbodies
                    RigidbodyNode.InvokeCollision(idA, idB, false);
                    RigidbodyNode.InvokeCollision(idB, idA, false);
                }
            }

            public override void EndContact(in Contact contact)
            {
                
            }

            public void PostSolve(Contact contact, ContactImpulse impulse)
            {
                
            }

            public override void PostSolve(in Contact contact, in ContactImpulse impulse)
            {
                
            }

            public void PreSolve(Contact contact, Manifold oldManifold)
            {
                
            }

            public override void PreSolve(in Contact contact, in Manifold oldManifold)
            {
                
            }
        }

        public class WorldDestructionListener : DestructionListener
        {
            public override void SayGoodbye(Joint joint)
            {
                throw new NotImplementedException();
            }

            public override void SayGoodbye(Fixture fixture)
            {
                foreach (var rigidbody in RigidbodyNode.Rigidbodies)
                {
                    if (rigidbody.ID == fixture.Body.GetUserData<uint>())
                    {
                        rigidbody.Dispose();
                    }
                }

                foreach (var sensor in RigidbodySensorNode.Sensors)
                {
                    if (sensor.ID == fixture.Body.GetUserData<uint>())
                    {
                        sensor.Dispose();
                    }
                }
            }
        }
    }
}
