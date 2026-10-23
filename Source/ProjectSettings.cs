using Atlas2D.UI;

namespace Atlas2D
{
    public static class ProjectSettings
    {
        // Physics -----------
        public static float PhysicsTimestep { get; private set; } = 0.016f;
        public static float PhysicsGravity { get; private set; } = -15f;
        public static int PhysicsVelocityIterations { get; private set; } = 6;
        public static int PhysicsPositionIterations { get; private set; } = 2;
        // -------------------

        // Networking --------
        public static ushort SteamPort { get; private set; } = 42420;
        public static int ServerHeartbeatIntervalMilliseconds { get; private set; } = 5000;
        // -------------------

        // Misc --------------
        public static UICanvasSettings UICanvasSettings { get; private set; } = new UICanvasSettings();
        public static CameraSettings CameraSettings { get; private set; } = new CameraSettings();
        public static bool GraphicsErrorCheckingEnabled { get; private set; } = false;
        public static bool ShowEngineSplashscreen { get; private set; } = true;
        public static bool UseHDRFrameBuffers { get; private set; } = false;
        public static string WindowTitle { get; private set; } = "Atlas2D Development Build";
        public static string EngineResourcePath { get; private set; } = "Resources/";
        // -------------------

        /// <summary>
        /// Applies optional overrides to the engine's default project settings. Must be applied before creating or running the Game instance, as engine subsystems will initialize using default values otherwise.
        /// </summary>
        /// <param name="overrides">
        /// An instance of <see cref="ProjectSettingsOverrides"> containing optional values
        /// to override engine defaults. Only non-null fields will be applied.
        /// </param>
        public static void ApplyOverrides(ProjectSettingsOverrides overrides)
        {
            if (overrides == null) return;

            // Physics
            if (overrides.PhysicsTimestep.HasValue) PhysicsTimestep = overrides.PhysicsTimestep.Value;
            if (overrides.PhysicsGravity.HasValue) PhysicsGravity = overrides.PhysicsGravity.Value;
            if (overrides.PhysicsVelocityIterations.HasValue) PhysicsVelocityIterations = overrides.PhysicsVelocityIterations.Value;
            if (overrides.PhysicsPositionIterations.HasValue) PhysicsPositionIterations = overrides.PhysicsPositionIterations.Value;

            // Networking
            if (overrides.SteamPort.HasValue) SteamPort = overrides.SteamPort.Value;
            if (overrides.ServerHeartbeatIntervalMilliseconds.HasValue) ServerHeartbeatIntervalMilliseconds = overrides.ServerHeartbeatIntervalMilliseconds.Value;

            // Misc
            if (overrides.UICanvasSettings != null) UICanvasSettings = overrides.UICanvasSettings;
            if (overrides.CameraSettings != null) CameraSettings = overrides.CameraSettings;
            if (overrides.GraphicsErrorCheckingEnabled.HasValue) GraphicsErrorCheckingEnabled = overrides.GraphicsErrorCheckingEnabled.Value;
            if (overrides.ShowEngineSplashscreen.HasValue) ShowEngineSplashscreen = overrides.ShowEngineSplashscreen.Value;
            if (overrides.UseHDRFrameBuffers.HasValue) UseHDRFrameBuffers = overrides.UseHDRFrameBuffers.Value;
            if (!string.IsNullOrEmpty(overrides.WindowTitle)) WindowTitle = overrides.WindowTitle;
            if (!string.IsNullOrEmpty(overrides.EngineResourcePath)) EngineResourcePath = overrides.EngineResourcePath;
        }
    }

    public class ProjectSettingsOverrides
    {
        // Physics -----------
        public float? PhysicsTimestep { get; set; }
        public float? PhysicsGravity { get; set; }
        public int? PhysicsVelocityIterations { get; set; }
        public int? PhysicsPositionIterations { get; set; }
        // -------------------

        // Networking --------
        public ushort? SteamPort { get; set; }
        public int? ServerHeartbeatIntervalMilliseconds { get; set; }
        // -------------------

        // Misc --------------
        public UICanvasSettings? UICanvasSettings { get; set; }
        public CameraSettings? CameraSettings { get; set; }
        public bool? GraphicsErrorCheckingEnabled { get; set; }
        public bool? ShowEngineSplashscreen { get; set; }
        public bool? UseHDRFrameBuffers { get; set; }
        public string? WindowTitle { get; set; }
        public string? EngineResourcePath { get; set; }
        // -------------------
    }
}