using Box2D.NetStandard.Dynamics.Fixtures;
using System.Numerics;

namespace Atlas2D.PhysicsBox2D
{
    public class RaycastHit
    {
        public bool Hit;
        public Vector2 Normal;
        public Vector2 Point;
        public float Fraction;
        public Fixture Fixture;
        /// <summary>
        /// The physics ID of the body that was hit.
        /// </summary>
        public uint BodyID = uint.MaxValue;
        /// <summary>
        /// The rigidbody that was hit, or null if the body isn't a <see cref="RigidbodyNode"/>.
        /// </summary>
        public RigidbodyNode Rigidbody => Hit ? RigidbodyNode.GetByID(BodyID) : null;
        public float Distance = 0;
        public float MaxDistance = 0;

        public RaycastHit()
        {
            Hit = false;
            Normal = Vector2.Zero;
            Point = Vector2.Zero;
            Fraction = 0;
            Fixture = null;
        }

        internal void Set(Fixture fixture, uint bodyID, Vector2 point, Vector2 normal, float fraction)
        {
            Hit = true;
            Fixture = fixture;
            BodyID = bodyID;
            Point = point;
            Normal = normal;
            Fraction = fraction;
            Distance = fraction * MaxDistance;
        }

        public string ToString()
        {
            return $"Hit: {Hit}, Normal: {Normal}, Point: {Point}, Fraction: {Fraction}, Distance: {Distance}, MaxDistance: {MaxDistance}";
        }
    }
}
