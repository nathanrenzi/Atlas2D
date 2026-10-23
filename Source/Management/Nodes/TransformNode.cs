using System.Numerics;

namespace Atlas2D
{
    public class TransformNode : Node
    {
        /// <summary>
        /// Fires when this node's local transform changes, or when a parent's transform changes.
        /// </summary>
        public event Action OnTransformChanged;

        private Vector2 _localPosition;
        private Vector2 _localScale = Vector2.One;
        private float _localRotation;

        // World values are saved here and only recalculated after something moves
        private bool _worldDirty = true;
        private Vector2 _worldPosition;
        private Vector2 _worldScale;
        private float _worldRotation;
        private Matrix4x4 _worldMatrix;

        public TransformNode() : base() { }

        /// <summary>
        /// Pass false to skip calling <see cref="Node.Load"/> here, then call it at the end of your own constructor.
        /// </summary>
        protected TransformNode(bool load) : base(load) { }

        public Vector2 LocalPosition
        {
            get => _localPosition;
            set { _localPosition = value; OnWorldTransformInvalidated(); }
        }

        public Vector2 LocalScale
        {
            get => _localScale;
            set { _localScale = value; OnWorldTransformInvalidated(); }
        }

        public float LocalRotation
        {
            get => _localRotation;
            set { _localRotation = value; OnWorldTransformInvalidated(); }
        }

        public Vector2 WorldPosition
        {
            get { UpdateWorldTransform(); return _worldPosition; }
        }

        public Vector2 WorldScale
        {
            get { UpdateWorldTransform(); return _worldScale; }
        }

        public float WorldRotation
        {
            get { UpdateWorldTransform(); return _worldRotation; }
        }

        public Matrix4x4 LocalMatrix =>
            Matrix4x4.CreateScale(LocalScale.X, LocalScale.Y, 1f) *
            Matrix4x4.CreateRotationZ(LocalRotation * MathF.PI / 180f) *
            Matrix4x4.CreateTranslation(LocalPosition.X, LocalPosition.Y, 0f);

        public Matrix4x4 WorldMatrix
        {
            get { UpdateWorldTransform(); return _worldMatrix; }
        }

        private float WorldRadians => WorldRotation * MathF.PI / 180f;
        public Vector2 Up => new(-MathF.Sin(WorldRadians), MathF.Cos(WorldRadians));
        public Vector2 Right => new(MathF.Cos(WorldRadians), MathF.Sin(WorldRadians));

        internal override void OnWorldTransformInvalidated()
        {
            _worldDirty = true;
            OnTransformChanged?.Invoke();
            base.OnWorldTransformInvalidated();
        }

        private void UpdateWorldTransform()
        {
            if (!_worldDirty) return;

            var parent = GetParentTransformNode();
            if (parent == null)
            {
                _worldMatrix = LocalMatrix;
                _worldPosition = LocalPosition;
                _worldRotation = LocalRotation;
                _worldScale = LocalScale;
            }
            else
            {
                _worldMatrix = LocalMatrix * parent.WorldMatrix;
                _worldPosition = new Vector2(_worldMatrix.M41, _worldMatrix.M42);
                _worldRotation = LocalRotation + parent.WorldRotation;
                _worldScale = LocalScale * parent.WorldScale;
            }

            _worldDirty = false;
        }

        private TransformNode? GetParentTransformNode()
        {
            var current = Parent;
            while (current != null)
            {
                if (current is TransformNode tn) return tn;
                current = current.Parent;
            }
            return null;
        }
    }
}