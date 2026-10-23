using Atlas2D.Rendering;
using Atlas2D.Rendering.PostProcessing;
using GLFW;
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using static Atlas2D.OpenGL.GL;

namespace Atlas2D
{
    public static class Display
    {
        public const float REFERENCE_WINDOW_WIDTH = 1920f;
        public const float REFERENCE_WINDOW_HEIGHT = 1080f;
        public static event Action OnWindowResize;

        public static GLFW.Window Window { get; private set; }
        public static GLFW.Monitor Monitor { get; private set; }
        public static Vector2 WindowSize { get; private set; }
        private static PostProcessingStack _fxaaPostProcessing = new(-1);
        private static SizeCallback _windowResizeCallback;
        private static MonitorCallback _monitorCallback;
        private static PositionCallback _windowPositionCallback;
        private static bool _monitorChanged;

        public static void Initialize()
        {
            if (!Glfw.Init())
                throw new FatalException($"Could not initialize the windowing system.\n\n{Glfw.LastErrorDescription}");
            AssignMonitorPointers();
        }

        public static void CreateWindow(int width, int height, string title)
        {
            Settings settings = Engine.Game.Settings;
            Monitor = settings.Monitor;

            if (Window != Window.None)
            {
                Debug.LogError("Cannot create a window. Multiple windows are not supported.");
                return;
            }

            if (width <= 0 || height <= 0)
            {
                width = settings.Monitor.WorkArea.Width;
                height = settings.Monitor.WorkArea.Height;
            }

            VideoMode mode = Glfw.GetVideoMode(settings.Monitor);

            Glfw.WindowHint(Hint.RedBits, mode.RedBits);
            Glfw.WindowHint(Hint.GreenBits, mode.GreenBits);
            Glfw.WindowHint(Hint.BlueBits, mode.BlueBits);
            Glfw.WindowHint(Hint.RefreshRate, Engine.Game.Settings.RefreshRate);
            Glfw.WindowHint(Hint.ContextVersionMajor, 3);
            Glfw.WindowHint(Hint.ContextVersionMinor, 3);
            Glfw.WindowHint(Hint.OpenglProfile, Profile.Core);
            Glfw.WindowHint(Hint.Focused, true);
            Glfw.WindowHint(Hint.Resizable, false);
            Glfw.WindowHint(Hint.Samples, 4);
            Glfw.WindowHint(Hint.SrgbCapable, true);
            Glfw.WindowHint(Hint.Maximized, settings.WindowMode == WindowMode.Fullscreen);

            Window = Glfw.CreateWindow(width, height, title, GLFW.Monitor.None, Window.None);

            if (Window == Window.None)
            {
                throw new FatalException("Could not create the game window. This game requires a graphics card that supports OpenGL 3.3. " +
                    $"Try updating your graphics drivers.\n\n{Glfw.LastErrorDescription}");
            }

            Glfw.MakeContextCurrent(Window);
            Import(Glfw.GetProcAddress);

            if (settings.Vsync)
            {
                Glfw.SwapInterval(1);
            }
            else
            {
                Glfw.SwapInterval(0);
            }

            if (File.Exists(Resources.GetEngineResourcePath("icon.ico")))
            {
                SharedResource<Texture2D> icon = Resources.GetTexture(Resources.GetEngineResourcePath("icon.ico"));
                SetIcon(icon.Value);
                icon.Release();
            }

            SetWindowMode(settings.WindowMode);

            _windowResizeCallback = new SizeCallback(OnFramebufferResize);
            Glfw.SetFramebufferSizeCallback(Window, _windowResizeCallback);

            _monitorCallback = new MonitorCallback((monitor, connection) => NativeCallbackGuard.Run(AssignMonitorPointers));
            Glfw.SetMonitorCallback(_monitorCallback);

            _windowPositionCallback = new PositionCallback((window, x, y) => NativeCallbackGuard.Run(UpdateCurrentMonitor));
            Glfw.SetWindowPositionCallback(Window, _windowPositionCallback);

            _fxaaPostProcessing.Add(new FXAAPostProcess());
        }

        /// <summary>
        /// Applies changes caused by the window moving to another monitor. Called once per frame.
        /// </summary>
        internal static void Update()
        {
            if (!_monitorChanged) return;
            _monitorChanged = false;

            WindowMode mode = Engine.Game.Settings.WindowMode;
            if (mode == WindowMode.Fullscreen || mode == WindowMode.BorderlessWindow)
                SetWindowMode(mode);
            Debug.Log($"Window moved to monitor {Monitor.UserPointer}, size is now {WindowSize.X}x{WindowSize.Y}");
        }

        private static void UpdateCurrentMonitor()
        {
            Glfw.GetWindowPosition(Window, out int x, out int y);
            Glfw.GetWindowSize(Window, out int width, out int height);
            int centerX = x + width / 2;
            int centerY = y + height / 2;

            foreach (GLFW.Monitor monitor in Glfw.Monitors)
            {
                Glfw.GetMonitorPosition(monitor, out int monitorX, out int monitorY);
                VideoMode video = Glfw.GetVideoMode(monitor);
                bool containsCenter = centerX >= monitorX && centerX < monitorX + video.Width
                    && centerY >= monitorY && centerY < monitorY + video.Height;
                if (!containsCenter) continue;

                if (monitor != Monitor)
                {
                    Monitor = monitor;
                    Engine.Game.Settings.Monitor = monitor;
                    _monitorChanged = true;
                }
                return;
            }
        }

        private static void AssignMonitorPointers()
        {
            for (int i = 0; i < Glfw.Monitors.Length; i++)
            {
                Glfw.Monitors[i].UserPointer = i;
            }
        }

        private static void OnFramebufferResize(Window window, int width, int height)
        {
            NativeCallbackGuard.Run(() => UpdateWindowSize(width, height));
        }

        private static void UpdateWindowSize(int width, int height)
        {
            // The size is 0 while the window is minimized
            if (width <= 0 || height <= 0) return;

            glViewport(0, 0, width, height);
            Vector2 size = new Vector2(width, height);
            if (size == WindowSize) return;
            WindowSize = size;
            OnWindowResize?.Invoke();
        }

        /// <summary>
        /// Can be used to set the icon of the window during runtime. Note: To change the icon of the .exe file and window at startup,
        /// be sure to place a default icon file at 'Resources/icon.ico'
        /// </summary>
        /// <param name="iconTexture">The texture of the icon to use. Note: Must be 16x16, 32x32, 48x48, 64x64, 128x128, or 256x256. GLFW will automatically
        /// resize if needed, so use a larger resolution.</param>
        public static void SetIcon(Texture2D iconTexture)
        {
            Bitmap bitmap = iconTexture.GetData(GL_RGBA);
            bitmap.RotateFlip(RotateFlipType.Rotate180FlipX);
            var data = bitmap.LockBits(
            new Rectangle(0, 0, iconTexture.Width, iconTexture.Height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);
            GLFW.Image image = new GLFW.Image(iconTexture.Width, iconTexture.Height, data.Scan0);
            Glfw.SetWindowIcon(Window, 1, [image]);
        }

        public static void SetWindowMode(WindowMode mode)
        {
            Settings settings = Engine.Game.Settings;
            Rectangle screen = Monitor.WorkArea;
            VideoMode video = Glfw.GetVideoMode(Monitor);
            switch (mode)
            {
                default:
                case WindowMode.Fullscreen:
                    Glfw.SetWindowMonitor(Window, Monitor, 0, 0, video.Width,
                        video.Height, settings.RefreshRate);
                    break;
                case WindowMode.Windowed:
                    Glfw.SetWindowAttribute(Window, WindowAttribute.Decorated, true);
                    Glfw.SetWindowMonitor(Window, GLFW.Monitor.None,
                        (screen.Width - settings.WindowWidth) / 2 + screen.X,
                        (screen.Height - settings.WindowHeight) / 2 + screen.Y,
                        settings.WindowWidth,
                        settings.WindowHeight, settings.RefreshRate);
                    break;
                case WindowMode.BorderlessWindow:
                    // Covers the whole monitor, including the taskbar
                    Glfw.GetMonitorPosition(Monitor, out int monitorX, out int monitorY);
                    Glfw.SetWindowAttribute(Window, WindowAttribute.Decorated, false);
                    Glfw.SetWindowMonitor(Window, GLFW.Monitor.None, monitorX, monitorY, video.Width,
                        video.Height, settings.RefreshRate);
                    break;
            }

            // Uses the real size the window ended up with
            Glfw.GetFramebufferSize(Window, out int width, out int height);
            UpdateWindowSize(width, height);
        }

        public static void SetAntialiasingMode(AntialiasingMode antialiasing)
        {
            switch (antialiasing)
            {
                case AntialiasingMode.None:
                    PostProcessor.Instance.Unregister(_fxaaPostProcessing);
                    glDisable(GL_MULTISAMPLE);
                    break;
                case AntialiasingMode.MSAA:
                    PostProcessor.Instance.Unregister(_fxaaPostProcessing);
                    glEnable(GL_MULTISAMPLE);
                    break;
                case AntialiasingMode.FXAA:
                    PostProcessor.Instance.Register(_fxaaPostProcessing);
                    break;
            }
        }

        public static void DestroyWindow()
        {
            if (Window == Window.None) return;
            Glfw.DestroyWindow(Window);
            Window = Window.None;
        }
    }
}
