using System.Numerics;

namespace Atlas2D
{
    public class CameraNode : TransformNode
    {
        /// <summary>
        /// The current main camera of the scene. Used for rendering and audio spatialization.
        /// </summary>
        public static CameraNode Main { get; private set; }

        public bool IsMain { get; private set; }
        public float Zoom;

        /// <summary>
        /// How many screen pixels one world unit covers, combining <see cref="Zoom"/> with <see cref="ProjectSettings.CameraSettings"/>.
        /// </summary>
        public float PixelsPerUnit => Zoom * GetWindowScale();

        public CameraNode(float zoom = 1)
        {
            Zoom = zoom;

            // Automatically set the main camera to this if null
            if(Main == null)
            {
                Main = this;
                IsMain = true;
            }
        }

        /// <summary>
        /// Sets this camera as the main camera, replacing the current <see cref="Main"/>.
        /// </summary>
        /// <param name="cameraNode"></param>
        public static void SetMain(CameraNode cameraNode)
        {
            if (Main == cameraNode) return;

            if (Main != null) Main.IsMain = false;
            Main = cameraNode;
            cameraNode.IsMain = true;
        }

        /// <summary>
        /// Converts world coordinates to view space (camera-relative).
        /// </summary>
        public Matrix4x4 GetViewMatrix()
        {
            Matrix4x4 translation = Matrix4x4.CreateTranslation(-WorldPosition.X, -WorldPosition.Y, 0);
            Matrix4x4 rotation = Matrix4x4.CreateRotationZ(-WorldRotation * MathF.PI / 180f);
            Matrix4x4 zoom = Matrix4x4.CreateScale(PixelsPerUnit, PixelsPerUnit, 1f);

            return translation * rotation * zoom;
        }

        /// <summary>
        /// Converts a position from screen pixels, measured from the top left of the window, to world space. Ignores camera rotation.
        /// </summary>
        public Vector2 ScreenToWorld(Vector2 screenPosition)
        {
            Vector2 centered = new Vector2(
                screenPosition.X - Display.WindowSize.X * 0.5f,
                (Display.WindowSize.Y * 0.5f) - screenPosition.Y
            );
            return centered / PixelsPerUnit + WorldPosition;
        }

        private static float GetWindowScale()
        {
            CameraSettings settings = ProjectSettings.CameraSettings;
            if (settings.ScalingMode == CameraScalingMode.ExpandWithWindow) return 1f;
            return MathF.Min(Display.WindowSize.X / settings.ViewSize.X, Display.WindowSize.Y / settings.ViewSize.Y);
        }

        /// <summary>
        /// Creates an orthographic projection based on the current window size.
        /// </summary>
        public Matrix4x4 GetProjectionMatrix()
        {
            float halfWidth = Display.WindowSize.X / 2f;
            float halfHeight = Display.WindowSize.Y / 2f;

            return Matrix4x4.CreateOrthographicOffCenter(
                -halfWidth, halfWidth,
                -halfHeight, halfHeight,
                -1f, 1f
            );
        }

        /// <summary>
        /// Creates a combined view-projection matrix used for rendering.
        /// </summary>
        /// <returns></returns>
        public Matrix4x4 GetViewProjectionMatrix()
        {
            return GetViewMatrix() * GetProjectionMatrix();
        }

        /// <summary>
        /// Creates an unscaled orthographic projection for UI or screen-space rendering.
        /// </summary>
        public Matrix4x4 GetUnscaledProjectionMatrix()
        {
            return Matrix4x4.CreateOrthographicOffCenter(
                0f, Display.WindowSize.X,
                Display.WindowSize.Y, 0f,
                -1f, 1f
            );
        }
    }
}
