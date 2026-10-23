using Box2D.NetStandard.Collision.Shapes;
using Box2D.NetStandard.Dynamics.Bodies;
using Box2D.NetStandard.Dynamics.Fixtures;
using Box2D.NetStandard.Dynamics.Joints;
using System.Numerics;

namespace Atlas2D.PhysicsBox2D
{
    public class RigidbodyNode : TransformNode
    {
        public static readonly float Epsilon = 1f;
        public static List<RigidbodyNode> Rigidbodies = new List<RigidbodyNode>();

        public Action<RigidbodyNode> OnBeginContact { get; set; }
        public Action<RigidbodyNode> OnEndContact { get; set; }
        public List<RigidbodyNode> CurrentContacts { get; set; } = new List<RigidbodyNode>();
        public Dictionary<uint, RigidbodyNode> Joints = new Dictionary<uint, RigidbodyNode>();
        public bool IsDestroyed { get; private set; }
        public uint ID { get; private set; } = uint.MaxValue;
        public Body PhysicsBody
        {
            get
            {
                return Physics.GetBody(ID);
            }
        }
        public Vector2 Velocity
        {
            get
            {
                return Physics.GetBodyVelocity(ID);
            }
            set
            {
                Physics.SetVelocity(ID, value);
            }
        }
        /// <summary>
        /// Instead of getting the velocity from the physics simulation, it is calculated using the last frame's
        /// and this frame's position. This can help when the rigidbody is behaving strangely and the velocity is
        /// not accurate.
        /// </summary>
        public Vector2 CalculatedVelocity
        {
            get
            {
                return _newPosition - _oldPosition;
            }
        }

        public float AngularVelocity
        {
            get
            {
                return Physics.GetBodyAngularVelocity(ID);
            }
            set
            {
                Physics.SetAngularVelocity(ID, value);
            }
        }
        public RigidbodyMode Mode { get; private set; }
        public RigidbodyShape Shape { get; private set; }
        public bool IsStatic { get; private set; }

        // https://www.iforce2d.net/b2dtut/collision-filtering
        /// <summary>
        /// The layer of this rigidbody determines how other rigidbodies will interact with this one.
        /// </summary>
        public ushort Layer { get; private set; }
        /// <summary>
        /// The hit mask determines what layers this rigidbody can collide with.
        /// </summary>
        public ushort HitMask { get; private set; }
        /// <summary>
        /// If either rigidbody has a GroupIndex of zero, use the category/mask rules as above.
        /// If both GroupIndex values are non-zero but different, use the category/mask rules as above.
        /// If both GroupIndex values are the same and positive, collide.
        /// If both GroupIndex values are the same and negative, don't collide.
        /// </summary>
        public short GroupIndex { get; private set; }
        /// <summary>
        /// Vertex array for a custom convex collider. Only used when <see cref="Shape"/> is set to <see cref="RigidbodyShape.ConvexMesh"/>
        /// </summary>
        public Vector2[] ConvexColliderPoints { get; private set; }

        // Starting Values
        private Vector2 _velocity;
        private float _angularVelocity;
        private float _linearDampening;
        private float _angularDampening;
        private float _density;
        private float _friction;
        private float _bounciness;
        private MassData _massData;
        private bool _fixedRotation;
        // ---------------

        private Vector2 _newPosition;
        private float _newAngle;
        private Vector2 _oldPosition;
        private float _oldAngle;
        private float _lerpDeltaTime;
        private float _lastLerpTime;

        private bool _isValid = false;
        private bool _interpolationReady = false;

        public static RigidbodyNode CreateDynamic(RigidbodyDynamicDef def)
        {
            return new RigidbodyNode(false, def.Velocity, def.AngularVelocity, def.MassData, def.Friction, def.Bounciness,
                def.Density, def.LinearDampening, def.AngularDampening, def.FixedRotation, def.Shape,
                RigidbodyMode.Dynamic, def.Layer, def.HitMask, def.GroupIndex, def.ConvexColliderPoints);
        }

        public static RigidbodyNode CreateKinematic(RigidbodyKinematicDef def)
        {
            return new RigidbodyNode(true, new Vector2(0, 0), 0, new MassData(), def.Friction, def.Bounciness, 1, 0.0f, 0.0f, false, def.Shape,
                RigidbodyMode.Kinematic, def.Layer, def.HitMask, def.GroupIndex, def.ConvexColliderPoints);
        }

        private RigidbodyNode(bool isStatic, Vector2 startVelocity, float startAngularVelocity,
            MassData massData, float friction, float bounciness, float density, float linearDampening,
            float angularDampening, bool fixedRotation, RigidbodyShape rigidbodyShape, RigidbodyMode rigidbodyMode,
            ushort layer, ushort hitMask, short groupIndex, Vector2[] convexColliderPoints)
        {
            IsStatic = isStatic;
            _velocity = startVelocity;
            _angularVelocity = startAngularVelocity;
            _linearDampening = linearDampening;
            _angularDampening = angularDampening;
            _friction = friction;
            _bounciness = bounciness;
            _density = density;
            _massData = massData;
            _fixedRotation = fixedRotation;
            Mode = rigidbodyMode;
            Shape = rigidbodyShape;
            Layer = layer;
            HitMask = hitMask;
            GroupIndex = groupIndex;
            ConvexColliderPoints = convexColliderPoints;

            Rigidbodies.Add(this);

            BodyDef bodyDef = new BodyDef()
            {
                position = LocalPosition / Physics.WorldScalar,
                angle = LocalRotation,
                linearVelocity = _velocity,
                angularVelocity = _angularVelocity,
                linearDamping = _linearDampening,
                angularDamping = _angularDampening,
                fixedRotation = _fixedRotation
            };

            FixtureDef fixtureDef = new FixtureDef()
            {
                density = _density,
                friction = _friction,
                restitution = _bounciness,
                filter = new Filter()
                {
                    categoryBits = Layer,
                    maskBits = HitMask,
                    groupIndex = GroupIndex
                }
            };
            switch (Shape)
            {
                case RigidbodyShape.Box:
                    fixtureDef.shape = new PolygonShape((LocalScale.X - Epsilon) / 2f / Physics.WorldScalar, (LocalScale.Y - Epsilon) / 2f / Physics.WorldScalar);
                    break;
                case RigidbodyShape.Circle:
                    fixtureDef.shape = new CircleShape()
                    {
                        Radius = (LocalScale.X - Epsilon) / 2f / Physics.WorldScalar
                    };
                    break;
                case RigidbodyShape.ConvexMesh:
                    fixtureDef.shape = new PolygonShape();
                    break;
            }

            ID = Physics.CreatePhysicsBody(bodyDef, fixtureDef, _massData, Mode == RigidbodyMode.Kinematic);
            _isValid = true;
        }

        protected override void OnDispose()
        {
            if (IsDestroyed) return;
            Rigidbodies.Remove(this);
            if (ID != uint.MaxValue) Physics.RemovePhysicsBody(ID);
            IsDestroyed = true;
        }

        public static void InvokeCollision(uint _id, uint _hitId, bool _beginContact)
        {
            RigidbodyNode body1 = null;
            RigidbodyNode body2 = null;
            for (int i = 0; i < Rigidbodies.Count; i++)
            {
                if (Rigidbodies[i].ID == _id)
                {
                    body1 = Rigidbodies[i];
                }

                if (Rigidbodies[i].ID == _hitId)
                {
                    body2 = Rigidbodies[i];
                }
            }
            if (body1 == null) return;
            if (body2 == null) return;

            if (_beginContact)
            {
                // Begin Contact
                body1.OnBeginContact?.Invoke(body2);
                body1.CurrentContacts.Add(body2);
            }
            else
            {
                // End Contact
                body1.OnEndContact?.Invoke(body1);
                body1.CurrentContacts.Remove(body1);
            }
        }

        protected override void OnFixedUpdate()
        {
            if (!_isValid || ID == uint.MaxValue || IsDestroyed) return;

            // Setting values for interpolation
            _oldPosition = LocalPosition;
            _oldAngle = LocalRotation;

            _newPosition = Physics.GetBodyPosition(ID);
            _newAngle = Physics.GetBodyRotation(ID);

            _lerpDeltaTime = Time.FixedDeltaTime;
            _lastLerpTime = (float)GLFW.Glfw.Time;

            _interpolationReady = true;
        }

        protected override void OnUpdate()
        {
            if (!_isValid || ID == uint.MaxValue || !_interpolationReady || IsDestroyed) return;

            // Interpolation
            float t = MathEx.Clamp01((Time.GameTime - _lastLerpTime) / _lerpDeltaTime);

            //      Position
            LocalPosition = Vector2.Lerp(_oldPosition, _newPosition, t);

            //      Rotation
            LocalRotation = (float)(_oldAngle * (1.0 - t) + (_newAngle * t));
        }

        public uint CreateJoint(IRigidbodyJointDef _jointDef)
        {
            if(_jointDef.RigidbodyA == null || _jointDef.RigidbodyB == null)
            {
                Debug.LogError("PHYSICS: Cannot create a joint when one or more rigidbodies are null.");
            }

            uint id;
            // The joint definition reads the Box2D bodies, so the physics thread can't step during this
            lock (Physics.WorldLock)
            {
                id = Physics.CreateJoint(_jointDef.GetPhysicsDefinition());
            }
            _jointDef.RigidbodyA.Joints.Add(id, _jointDef.RigidbodyB);
            _jointDef.RigidbodyB.Joints.Add(id, _jointDef.RigidbodyA);
            return id;
        }

        public void RemoveJoint(uint _id)
        {
            if (!Joints.ContainsKey(_id)) return;
            Joints[_id].Joints.Remove(_id); // Removing from paired body
            Joints.Remove(_id); // Removing from this body
            Physics.RemoveJoint(_id);
        }

        public Joint GetJoint(uint _id)
        {
            // Only returning the joint object if this rigidbody is connected to it
            if(Joints.ContainsKey(_id))
            {
                return Physics.GetJoint(_id);
            }
            else
            {
                return null;
            }
        }
    }

    public enum RigidbodyShape
    {
        Box,
        Circle,
        ConvexMesh
    }

    public enum RigidbodyMode
    {
        Kinematic,
        Dynamic,
    }

    /// <summary>
    /// Defines a static rigidbody.
    /// </summary>
    public struct RigidbodyKinematicDef
    {
        /// <summary>
        /// The friction of the rigidbody against other rigidbodies.
        /// </summary>
        public float Friction = 1;
        /// <summary>
        /// The bounciness of the rigidbody. Also called Restitution.
        /// </summary>
        public float Bounciness = 0;
        /// <summary>
        /// The shape of the rigidbody.
        /// </summary>
        public RigidbodyShape Shape = RigidbodyShape.Box;
        /// <summary>
        /// The layer of this rigidbody determines how other rigidbodies will interact with this one.
        /// </summary>
        public ushort Layer = 0x0001;
        /// <summary>
        /// The hit mask determines what layers this rigidbody can collide with.
        /// </summary>
        public ushort HitMask = 0xFFFF;
        /// <summary>
        /// If either rigidbody has a GroupIndex of zero, use the category/mask rules as above.
        /// If both GroupIndex values are non-zero but different, use the category/mask rules as above.
        /// If both GroupIndex values are the same and positive, collide.
        /// If both GroupIndex values are the same and negative, don't collide.
        /// </summary>
        public short GroupIndex = 0;
        /// <summary>
        /// Vertex array for a custom convex collider. Only used when <see cref="Shape"/> is set to <see cref="RigidbodyShape.ConvexMesh"/>
        /// </summary>
        public Vector2[] ConvexColliderPoints = new Vector2[0];

        public RigidbodyKinematicDef() { }
    }

    /// <summary>
    /// Defines a dynamic rigidbody.
    /// </summary>
    public struct RigidbodyDynamicDef
    {
        /// <summary>
        /// The starting linear velocity of the rigidbody.
        /// </summary>
        public Vector2 Velocity = Vector2.Zero;
        /// <summary>
        /// The starting angular velocity of the rigidbody.
        /// </summary>
        public float AngularVelocity = 0;
        /// <summary>
        /// The friction of the rigidbody against other rigidbodies.
        /// </summary>
        public float Friction = 1;
        /// <summary>
        /// The bounciness of the rigidbody. Also called Restitution.
        /// </summary>
        public float Bounciness = 0;
        /// <summary>
        /// The dampening of the rigidbody's linear velocity.
        /// </summary>
        public float LinearDampening = 0.0f;
        /// <summary>
        /// The dampening of the rigidbody's angular velocity.
        /// </summary>
        public float AngularDampening = 0.01f;
        /// <summary>
        /// The density of the rigidbody./>
        /// </summary>
        public float Density = 1;
        /// <summary>
        /// The mass data of the rigidbody.
        /// </summary>
        public MassData MassData = new MassData();
        /// <summary>
        /// Whether the rigidbody can rotate in the simulation.
        /// </summary>
        public bool FixedRotation = false;
        /// <summary>
        /// Whether the physics simulation should use continuous detection when this dynamic rigidbody hits another dynamic rigidbody.
        /// This is set to false by default because it is costly to use for all dynamic objects, so only enable this when the rigidbody will be moving very quickly and
        /// hitting other dynamic rigidbodies.
        /// </summary>
        public bool IsBullet = false;
        /// <summary>
        /// The shape of the rigidbody.
        /// </summary>
        public RigidbodyShape Shape = RigidbodyShape.Box;
        /// <summary>
        /// The layer of this rigidbody determines how other rigidbodies will interact with this one.
        /// </summary>
        public ushort Layer = 0x0001;
        /// <summary>
        /// The hit mask determines what layers this rigidbody can collide with.
        /// </summary>
        public ushort HitMask = 0xFFFF;
        /// <summary>
        /// If either rigidbody has a GroupIndex of zero, use the category/mask rules as above.
        /// If both GroupIndex values are non-zero but different, use the category/mask rules as above.
        /// If both GroupIndex values are the same and positive, collide.
        /// If both GroupIndex values are the same and negative, don't collide.
        /// </summary>
        public short GroupIndex = 0;
        /// <summary>
        /// Vertex array for a custom convex collider. Only used when <see cref="Shape"/> is set to <see cref="RigidbodyShape.ConvexMesh"/>
        /// </summary>
        public Vector2[] ConvexColliderPoints = new Vector2[0];

        public RigidbodyDynamicDef() { }
    }
}
