using Box2D.NetStandard.Collision.Shapes;
using Box2D.NetStandard.Dynamics.Bodies;
using Box2D.NetStandard.Dynamics.Fixtures;
using System.Numerics;

namespace Atlas2D.PhysicsBox2D
{
    public class RigidbodySensorNode : TransformNode
    {
        public static readonly float Epsilon = 1f;
        public static List<RigidbodySensorNode> Sensors = new List<RigidbodySensorNode>();
        private static readonly Dictionary<uint, RigidbodySensorNode> _sensorsByID = new Dictionary<uint, RigidbodySensorNode>();

        public Action<RigidbodyNode> OnBeginContact { get; set; }
        public Action<RigidbodyNode> OnEndContact { get; set; }
        public List<RigidbodyNode> CurrentContacts { get; set; } = new List<RigidbodyNode>();

        public uint ID { get; private set; } = uint.MaxValue;
        public RigidbodySensorShape Shape { get; private set; }
        public Vector2 Size { get; private set; }
        public Vector2 Offset { get; private set; }

        // https://www.iforce2d.net/b2dtut/collision-filtering
        /// <summary>
        /// The layer of this sensor determines how rigidbodies will interact with it.
        /// </summary>
        public ushort Layer { get; private set; }
        /// <summary>
        /// The hit mask determines what layers this sensor can collide with.
        /// </summary>
        public ushort HitMask { get; private set; }
        /// <summary>
        /// If either body has a GroupIndex of zero, use the category/mask rules as above.
        /// If both GroupIndex values are non-zero but different, use the category/mask rules as above.
        /// If both GroupIndex values are the same and positive, collide.
        /// If both GroupIndex values are the same and negative, don't collide.
        /// </summary>
        public short GroupIndex { get; private set; }

        private bool _isValid;

        public RigidbodySensorNode(Vector2 size, Vector2 localOffset = default, RigidbodySensorShape shape = RigidbodySensorShape.Circle,
            ushort layer = 0x0001, ushort hitMask = 0xFFFF, short groupIndex = 0)
        {
            Shape = shape;
            Size = size;
            Offset = localOffset;
            Layer = layer;
            HitMask = hitMask;
            GroupIndex = groupIndex;

            Sensors.Add(this);

            Vector2 pos = WorldPosition + (Up * Offset.Y) + (Right * Offset.X);
            BodyDef bodyDef = new BodyDef()
            {
                position = pos / Physics.WorldScalar,
                angle = WorldRotation * MathF.PI / 180f,
            };

            FixtureDef fixtureDef = new FixtureDef()
            {
                filter = new Filter()
                {
                    categoryBits = Layer,
                    maskBits = HitMask,
                    groupIndex = GroupIndex
                },
                isSensor = true,
            };
            switch (Shape)
            {
                case RigidbodySensorShape.Box:
                    fixtureDef.shape = new PolygonShape((Size.X - Epsilon) / 2f / Physics.WorldScalar,
                        (Size.Y - Epsilon) / 2f / Physics.WorldScalar);
                    break;
                case RigidbodySensorShape.Circle:
                    fixtureDef.shape = new CircleShape()
                    {
                        Radius = (Size.X - Epsilon) / 2f / Physics.WorldScalar,
                    };
                    break;
            }

            ID = Physics.CreatePhysicsBody(bodyDef, fixtureDef, new MassData(), true);
            _isValid = ID != uint.MaxValue;
            if (_isValid) _sensorsByID[ID] = this;
        }

        protected override void OnDispose()
        {
            Sensors.Remove(this);
            CurrentContacts.Clear();
            if (ID != uint.MaxValue)
            {
                _sensorsByID.Remove(ID);
                Physics.RemovePhysicsBody(ID);
            }
        }

        /// <summary>
        /// Ends the contact with a rigidbody, if there is one. Used when the rigidbody is disposed.
        /// </summary>
        internal void RemoveContact(RigidbodyNode rigidbody)
        {
            if (CurrentContacts.Remove(rigidbody))
            {
                OnEndContact?.Invoke(rigidbody);
            }
        }

        public static void InvokeCollision(uint _id, uint _hitId, bool _beginContact)
        {
            RigidbodyNode hitBody = RigidbodyNode.GetByID(_hitId);
            if (hitBody == null) return;

            if (!_sensorsByID.TryGetValue(_id, out RigidbodySensorNode sensor)) return;

            if(_beginContact)
            {
                // Begin Contact
                sensor.CurrentContacts.Add(hitBody);
                sensor.OnBeginContact?.Invoke(hitBody);
            }
            else
            {
                // End Contact
                sensor.RemoveContact(hitBody);
            }
        }

        protected override void OnUpdate()
        {
            if (!_isValid || ID == uint.MaxValue) return;

            Physics.SetAngle(ID, WorldRotation);
            Physics.SetPosition(ID, WorldPosition + (Up * Offset.Y) + (Right * Offset.X));
        }
    }

    public enum RigidbodySensorShape
    {
        Box,
        Circle
    }
}
