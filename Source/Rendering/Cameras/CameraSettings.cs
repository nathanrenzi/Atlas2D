using System.Numerics;

namespace Atlas2D
{
    /// <summary>
    /// Defines how much of the world the camera shows relative to the window size.
    /// </summary>
    public enum CameraScalingMode
    {
        /// <summary>
        /// The world is scaled so <see cref="CameraSettings.ViewSize"/> always fits in the window, so every window shows the same area.
        /// Windows with a different aspect ratio see a little extra on the sides or the top and bottom.
        /// </summary>
        FixedViewSize,

        /// <summary>
        /// One world unit is one screen pixel at a zoom of 1, so larger windows show more of the world.
        /// </summary>
        ExpandWithWindow
    }

    /// <summary>
    /// Defines the scaling settings for every <see cref="CameraNode"/>.
    /// </summary>
    public sealed class CameraSettings
    {
        /// <summary>
        /// The scaling mode used to map the world to the window. Defaults to <see cref="CameraScalingMode.FixedViewSize"/>.
        /// </summary>
        public CameraScalingMode ScalingMode = CameraScalingMode.FixedViewSize;

        /// <summary>
        /// The area of the world, in world units, that fits in the window at a zoom of 1. Only used when <see cref="ScalingMode"/> is <see cref="CameraScalingMode.FixedViewSize"/>. Defaults to 1920x1080.
        /// </summary>
        public Vector2 ViewSize = new Vector2(1920, 1080);
    }
}
