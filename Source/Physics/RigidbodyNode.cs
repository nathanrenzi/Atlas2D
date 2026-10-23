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
        private static readonly Dictionary<uint, RigidbodyNode> _rigidbodiesByID = new Dictionary<uint, RigidbodyNode>();

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
        /// <summary>
        /// The size of the collider in pixels. For circles, X is the diameter.
        /// </summary>
        public Vector2 Size { get; private set; }
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
        /// Vertex array for a custom convex collider, in pixels around the body's center (3 to 8 points). Only used when <see cref="Shape"/> is set to <see cref="RigidbodyShape.ConvexMesh"/>
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
            return new RigidbodyNode(false, def.Position, def.Rotation, def.Size, def.Velocity, def.AngularVelocity, def.MassData,
                def.Friction, def.Bounciness, def.Density, def.LinearDampening, def.AngularDampening, def.FixedRotation, def.IsBullet,
                def.Shape, RigidbodyMode.Dynamic, def.Layer, def.HitMask, def.GroupIndex, def.ConvexColliderPoints);
        }

        public static RigidbodyNode CreateKinematic(RigidbodyKinematicDef def)
        {
            return new RigidbodyNode(true, def.Position, def.Rotation, def.Size, new Vector2(0, 0), 0, new MassData(),
                def.Friction, def.Bounciness, 1, 0.0f, 0.0f, false, false,
                def.Shape, RigidbodyMode.Kinematic, def.Layer, def.HitMask, def.GroupIndex, def.ConvexColliderPoints);
        }

        private RigidbodyNode(bool isStatic, Vector2 position, float rotation, Vector2 size, Vector2 startVelocity,
            float startAngularVelocity, MassData massData, float friction, float bounciness, float density,
            float linearDampening, float angularDampening, bool fixedRotation, bool isBullet, RigidbodyShape rigidbodyShape,
            RigidbodyMode rigidbodyMode, ushort layer, ushort hitMask, short groupIndex, Vector2[] convexColliderPoints)
        {
            // Set first, because the Box2D body is built from them below
            LocalPosition = position;
            LocalRotation = rotation;
            Size = size;

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
                angle = LocalRotation * MathF.PI / 180f,
                linearVelocity = _velocity / Physics.WorldScalar,
                angularVelocity = _angularVelocity * MathF.PI / 180f,
                linearDamping = _linearDampening,
                angularDamping = _angularDampening,
                fixedRotation = _fixedRotation,
                bullet = isBullet
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
            // The collider is made slightly smaller than Size (by Epsilon pixels), and never smaller than 1 pixel
            Vector2 halfSize = Vector2.Max((Size - new Vector2(Epsilon)) / 2f, new Vector2(0.5f)) / Physics.WorldScalar;
            switch (Shape)
            {
                case RigidbodyShape.Box:
                    fixtureDef.shape = new PolygonShape(halfSize.X, halfSize.Y);
                    break;
                case RigidbodyShape.Circle:
                    fixtureDef.shape = new CircleShape()
                    {
                        Radius = halfSize.X
                    };
                    break;
                case RigidbodyShape.ConvexMesh:
                    if (ConvexColliderPoints == null || ConvexColliderPoints.Length < 3 || ConvexColliderPoints.Length > 8)
                    {
                        Debug.LogError("PHYSICS: A convex mesh collider needs 3 to 8 points. Using a box instead.");
                        fixtureDef.shape = new PolygonShape(halfSize.X, halfSize.Y);
                    }
                    else
                    {
                        Vector2[] points = new Vector2[ConvexColliderPoints.Length];
                        for (int i = 0; i < points.Length; i++)
                        {
                            points[i] = ConvexColliderPoints[i] / Physics.WorldScalar;
                        }
                        fixtureDef.shape = new PolygonShape(points);
                    }
                    break;
            }

            ID = Physics.CreatePhysicsBody(bodyDef, fixtureDef, _massData, Mode == RigidbodyMode.Kinematic);
            _isValid = ID != uint.MaxValue;
            if (_isValid) _rigidbodiesByID[ID] = this;
        }

        protected override void OnDispose()
        {
            if (IsDestroyed) return;
            IsDestroyed = true;

            // Joints are removed first, so the other body's joint list stays correct
            foreach (uint jointID in Joints.Keys.ToArray())
            {
                RemoveJoint(jointID);
            }

            // End any current contacts now. Box2D's own end events for this body arrive after it's gone, and get ignored.
            foreach (RigidbodyNode other in CurrentContacts.ToArray())
            {
                if (other.CurrentContacts.Remove(this))
                {
                    other.OnEndContact?.Invoke(this);
                }
            }
            CurrentContacts.Clear();
            foreach (RigidbodySensorNode sensor in RigidbodySensorNode.Sensors.ToArray())
            {
                sensor.RemoveContact(this);
            }

            Rigidbodies.Remove(this);
            if (ID != uint.MaxValue)
            {
                _rigidbodiesByID.Remove(ID);
                Physics.RemovePhysicsBody(ID);
            }
        }

        /// <summary>
        /// Returns the rigidbody with the given physics ID, or null.
        /// </summary>
        public static RigidbodyNode GetByID(uint _id)
        {
            return _rigidbodiesByID.TryGetValue(_id, out RigidbodyNode rigidbody) ? rigidbody : null;
        }

        public static void InvokeCollision(uint _id, uint _hitId, bool _beginContact)
        {
            RigidbodyNode body1 = GetByID(_id);
            RigidbodyNode body2 = GetByID(_hitId);
            if (body1 == null) return;
            if (body2 == null) return;

            if (_beginContact)
            {
                // Begin Contact
                body1.CurrentContacts.Add(body2);
                body1.OnBeginContact?.Invoke(body2);
            }
            else
            {
                // End Contact
                if (body1.CurrentContacts.Remove(body2))
                {
                    body1.OnEndContact?.Invoke(body2);
                }
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
                return uint.MaxValue;
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
        /// The starting position of the rigidbody, in pixels.
        /// </summary>
        public Vector2 Position = Vector2.Zero;
        /// <summary>
        /// The starting rotation of the rigidbody, in degrees.
        /// </summary>
        public float Rotation = 0;
        /// <summary>
        /// The size of the collider in pixels. For circles, X is the diameter.
        /// </summary>
        public Vector2 Size = new Vector2(32, 32);
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
        /// Vertex array for a custom convex collider, in pixels around the body's center (3 to 8 points). Only used when <see cref="Shape"/> is set to <see cref="RigidbodyShape.ConvexMesh"/>
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
        /// The starting position of the rigidbody, in pixels.
        /// </summary>
        public Vector2 Position = Vector2.Zero;
        /// <summary>
        /// The starting rotation of the rigidbody, in degrees.
        /// </summary>
        public float Rotation = 0;
        /// <summary>
        /// The size of the collider in pixels. For circles, X is the diameter.
        /// </summary>
        public Vector2 Size = new Vector2(32, 32);
        /// <summary>
        /// The starting linear velocity of the rigidbody, in pixels per second.
        /// </summary>
        public Vector2 Velocity = Vector2.Zero;
        /// <summary>
        /// The starting angular velocity of the rigidbody, in degrees per second.
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
        /// The mass data of the rigidbody. Leave the mass at 0 to calculate it from the density and shape.
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
        /// Vertex array for a custom convex collider, in pixels around the body's center (3 to 8 points). Only used when <see cref="Shape"/> is set to <see cref="RigidbodyShape.ConvexMesh"/>
        /// </summary>
        public Vector2[] ConvexColliderPoints = new Vector2[0];

        public RigidbodyDynamicDef() { }
    }
}
