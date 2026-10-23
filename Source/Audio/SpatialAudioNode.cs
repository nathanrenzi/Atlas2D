using System.Numerics;

namespace Atlas2D.Audio
{
    public class SpatialAudioNode : TransformNode
    {
        public AudioInstance AudioInstance { get; }
        public float MinRange { get; set; } = 100f;
        public float MaxRange { get; set; } = Display.REFERENCE_WINDOW_WIDTH * 0.5f;
        public float PanningSpatializationMultiplier { get; set; } = 1.0f;
        public float VolumeSpatializationMultiplier { get; set; } = 1.0f;
        public Curve FalloffCurve { get; set; } = new Curve(new List<Curve.Point>
        {
            new Curve.Point(0, 1, Curve.Handle.Left, Curve.Handle.Right),
            new Curve.Point(1, 0, Curve.Handle.Left, Curve.Handle.Right)
        });

        public SpatialAudioNode(AudioInstance audioInstance)
        {
            AudioInstance = audioInstance;
        }

        protected override void OnEnable() => AudioInstance.Unpause();
        protected override void OnDisable() => AudioInstance.Pause();

        protected override void OnUpdate()
        {
            if (CameraNode.Main == null) return;
            Vector2 listenerPos = CameraNode.Main.WorldPosition;
            float rawDistance = Vector2.Distance(listenerPos, WorldPosition);
            float range = MathF.Max(MaxRange - MinRange, 0.001f);
            float normalizedDistance = MathEx.Clamp01((rawDistance - MinRange) / range);
            float volumeMultiplier = FalloffCurve.Evaluate(normalizedDistance * MathEx.Clamp(VolumeSpatializationMultiplier, 0, 1));
            float visibleHalfWidth = (Display.REFERENCE_WINDOW_WIDTH * 0.5f) / CameraNode.Main.Zoom;
            float panning = ((WorldPosition.X - listenerPos.X) / visibleHalfWidth) * MathEx.Clamp(PanningSpatializationMultiplier, 0, 10);
            AudioInstance.Stream?.SetSpatializationValues(volumeMultiplier, panning);
        }

        protected override void OnDispose()
        {
            AudioInstance.Stop();
            AudioInstance.Dispose();
        }
    }
}
