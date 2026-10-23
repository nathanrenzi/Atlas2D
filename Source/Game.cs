using Atlas2D.Audio;
using Atlas2D.Misc;
using Atlas2D.Networking;
using Atlas2D.PhysicsBox2D;
using Atlas2D.Rendering;
using Atlas2D.Rendering.PostProcessing;
using Atlas2D.Rendering.Shaders;
using Atlas2D.UI;
using GLFW;

using System.Numerics;
using System.Runtime.InteropServices;
using static Atlas2D.OpenGL.GL;

namespace Atlas2D
{
    public abstract class Game
    {
        public event Action LateUpdateEvent;

        public Settings Settings { get; private set; }
        public Color BackgroundColor { get; private set; } = Color.Black;
        public Vector4 LinearBackgroundColor { get; private set; } = Vector4.Zero;

        // Physics can fall behind during a hitch. These stop it from trying to catch up forever.
        private const int MAX_PHYSICS_STEPS_PER_TICK = 5;
        private const int MAX_FIXED_UPDATES_PER_FRAME = 5;

        protected Thread PhysicsThread { get; private set; }
        protected CancellationTokenSource PhysicsCancellationToken { get; private set; } = new();
        protected CameraNode DefaultCamera { get; set; }

        // Steps finished by the physics thread that haven't had a FixedUpdate yet
        private int _pendingFixedUpdates = 0;
        // _classes keeps update order, and the sets allow for O(1) Contains() calls
        private List<IGameClass> _classes = new List<IGameClass>();
        private HashSet<IGameClass> _classesSet = new HashSet<IGameClass>();
        private List<IGameClass> _classesAddQueue = new List<IGameClass>();
        private HashSet<IGameClass> _classesAddSet = new HashSet<IGameClass>();
        private HashSet<IGameClass> _classesRemoveQueue = new HashSet<IGameClass>();
        private readonly Predicate<IGameClass> _isQueuedForRemoval;
        private bool _useClassesQueue = false;
        private BlendMode _currentBlendMode = BlendMode.Interpolative;
        private bool _isGraphicsInitialized = false;
        private bool _hasExited = false;
        private bool _exitRequested = false;

        protected Game()
        {
            // Cached so PopClassesQueue doesn't allocate a delegate every frame
            _isQueuedForRemoval = c => _classesRemoveQueue.Contains(c);
        }

        private void PopClassesQueue()
        {
            if (_classesRemoveQueue.Count > 0)
            {
                _classes.RemoveAll(_isQueuedForRemoval);
                foreach (IGameClass gameClass in _classesRemoveQueue)
                {
                    _classesSet.Remove(gameClass);
                }
                _classesRemoveQueue.Clear();
            }
            for (int i = 0; i < _classesAddQueue.Count; i++)
            {
                if (_classesSet.Add(_classesAddQueue[i]))
                    _classes.Add(_classesAddQueue[i]);
            }
            _classesAddQueue.Clear();
            _classesAddSet.Clear();
        }

        public void RegisterGameClass(IGameClass gameClass)
        {
            if (_useClassesQueue)
            {
                // Cancel a removal queued earlier this frame (RemoveChild then RegisterGameClass)
                _classesRemoveQueue.Remove(gameClass);
                if (!_classesSet.Contains(gameClass) && _classesAddSet.Add(gameClass))
                    _classesAddQueue.Add(gameClass);
            }
            else if (_classesSet.Add(gameClass))
            {
                _classes.Add(gameClass);
            }
        }

        public void UnregisterGameClass(IGameClass gameClass)
        {
            if (_useClassesQueue)
            {
                // Added and removed in the same frame, so it never reached _classes
                if (_classesAddSet.Remove(gameClass))
                {
                    _classesAddQueue.Remove(gameClass);
                    return;
                }
                if (_classesSet.Contains(gameClass))
                    _classesRemoveQueue.Add(gameClass);
            }
            else
            {
                if (_classesAddSet.Remove(gameClass))
                    _classesAddQueue.Remove(gameClass);
                if (_classesSet.Remove(gameClass))
                    _classes.Remove(gameClass);
            }
        }

        public void SetBackgroundColor(Color backgroundColor)
        {
            BackgroundColor = backgroundColor;
            LinearBackgroundColor = new Vector4(MathF.Pow(backgroundColor.R, 2.2f), MathF.Pow(backgroundColor.G, 2.2f), MathF.Pow(backgroundColor.B, 2.2f), backgroundColor.A);
        }

        public BlendMode BlendingMode => _currentBlendMode;

        public void SetBlendingMode(BlendMode blendMode)
        {
            if (_currentBlendMode == blendMode) return;
            _currentBlendMode = blendMode;
            if (_isGraphicsInitialized) ApplyBlendingMode();
        }

        private void ApplyBlendingMode()
        {
            switch (_currentBlendMode)
            {
                case BlendMode.Interpolative:
                    glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
                    break;
                case BlendMode.Additive:
                    glBlendFunc(GL_ONE, GL_ONE);
                    break;
                case BlendMode.Multiplicative:
                    glBlendFunc(GL_DST_COLOR, GL_ZERO);
                    break;
                default:
                    glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
                    break;
            }
        }

        public void Run()
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            try
            {
                RunGame();
            }
            catch (System.Exception e)
            {
                Crash(e);
            }
        }

        private void RunGame()
        {
            Debug.OpenLogFile();
            Debug.Log("Starting initialization...");
            Engine.Initialize(this);
            Display.Initialize();
            Settings = Settings.Load();
            Cursor.Initialize();
            AudioSystem.Initialize(Settings.AudioMasterVolume);

            // Starting Physics Thread
            PhysicsThread = new Thread(() => RunPhysicsThread(PhysicsCancellationToken.Token, ProjectSettings.PhysicsTimestep,
                ProjectSettings.PhysicsVelocityIterations, ProjectSettings.PhysicsPositionIterations))
            {
                Name = "Physics",
                IsBackground = true
            };

            Initialize();
            if (_exitRequested)
            {
                Shutdown();
                return;
            }

            DefaultCamera = new CameraNode();

            Display.CreateWindow(Settings.WindowWidth, Settings.WindowHeight, ProjectSettings.WindowTitle);
            Input.Initialize();

            // Setup
            glEnable(GL_BLEND);
            glEnable(GL_STENCIL_TEST);
            glEnable(GL_FRAMEBUFFER_SRGB);
            if (glGetFramebufferAttachmentParameteriv(GL_FRAMEBUFFER, GL_BACK_LEFT, GL_FRAMEBUFFER_ATTACHMENT_COLOR_ENCODING, 1)[0] != GL_SRGB)
                Debug.LogWarning($"The window framebuffer is not sRGB on {glGetString(GL_RENDERER)}, colors will appear too dark");
            ApplyBlendingMode();
            _isGraphicsInitialized = true;
            // -----------

            GlobalShaders.Initialize();
            Material.Initialize();

            #region Splashscreen
            if (ProjectSettings.ShowEngineSplashscreen)
            {
                // Displaying splashscreen
                Debug.Log("Displaying splashscreen...");
                Splashscreen.Initialize();
                SharedResource<Texture2D> splashscreenTexture = Resources.GetTexture(Resources.GetEngineResourcePath("Textures/Splashscreen.png"), false);
                float splashscreenStartTime = (float)Glfw.Time;
                float splashscreenDisplayTime = 4f;
                float fadeTimePercentage = 0.3f;
                float bufferTime = 0.5f;
                float currentTime = -bufferTime;
                bool hasPlayedAudio = false;
                AudioInstance splashscreenAudio = AudioSystem.CreateInstance(Resources.GetEngineResourcePath("Audio/EngineRiff.mp3"), volume: 0.3f);
                while (!Glfw.WindowShouldClose(Display.Window) && (currentTime - bufferTime) < splashscreenDisplayTime)
                {
                    Input.ProcessInput(); // Letting the window know the program is responding

                    float t = MathEx.Clamp01((currentTime - bufferTime) / splashscreenDisplayTime);
                    float e;
                    if (t <= fadeTimePercentage)
                    {
                        e = -0.5f * (MathF.Cos(MathF.PI * (t / fadeTimePercentage)) - 1f);
                    }
                    else if (t >= 1 - fadeTimePercentage)
                    {
                        e = -0.5f * (MathF.Cos(MathF.PI * ((1 - t) / fadeTimePercentage)) - 1f);
                    }
                    else
                    {
                        e = 1;
                    }

                    glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT | GL_STENCIL_BUFFER_BIT);
                    glClearColor(0, 0, 0, 1);
                    Splashscreen.Render(splashscreenTexture, e);
                    if (t > fadeTimePercentage / 2f && !hasPlayedAudio)
                    {
                        hasPlayedAudio = true;
                        splashscreenAudio.Play();
                    }
                    Glfw.SwapBuffers(Display.Window);

                    currentTime = (float)Glfw.Time - splashscreenStartTime;
                }
                splashscreenAudio.Dispose();
                Splashscreen.Dispose();
                splashscreenTexture.Release();
                Debug.Log("Splashscreen ended");
            }
            #endregion

            // Initializing physics thread
            Physics.Initialize(new Vector2(0, ProjectSettings.PhysicsGravity), true);
            PhysicsThread.Start();

            Network.RegisterAll();

            ShaderGlobalUniforms.RegisterGlobalUniform("lights", LightManager.Instance);
            ShaderGlobalUniforms.RegisterGlobalUniform("time", TimeUniform.Instance);

            UICanvas uiCanvas = new UICanvas();
            PostProcessor.Instance.Initialize();
            Display.SetAntialiasingMode(Settings.AntialiasingMode);
            GC.Collect();

            Debug.Log("Initialization complete");
            Debug.Log("#################### GAME STARTED ####################", ConsoleColor.DarkGreen);

            Load();

            // Rendering before the game loop prevents a black screen when the window is opened
            GLClear();
            RenderCall();

            while (!Glfw.WindowShouldClose(Display.Window))
            {
                Time.DeltaTime = (float)Glfw.Time - Time.GameTime;
                Time.GameTime = (float)Glfw.Time;
                PerformanceTimings.FramesPerSecond = 1 / Time.DeltaTime;

                // Input
                Input.ProcessInput();
                // -----------------------

                // Updating
                double goST = Glfw.Time;
                Update();
                ShaderGlobalUniforms.UpdateShaders();
                _useClassesQueue = true;
                foreach (IGameClass gameClass in _classes)
                {
                    // Skip classes removed earlier this frame (re-parented or disposed)
                    if (_classesRemoveQueue.Contains(gameClass)) continue;
                    gameClass.Update();
                }
                _useClassesQueue = false;
                PopClassesQueue();
                AudioSystem.Update();
                LateUpdateEvent?.Invoke();
                PerformanceTimings.GameObjectMilliseconds = (Glfw.Time - goST) * 1000;
                // --------------------------

                // Physics
                double phyST = Glfw.Time;
                // Collision callbacks run here before FixedUpdate
                Physics.DispatchContactEvents();
                // One FixedUpdate per physics step since the last frame
                int fixedUpdates = Math.Min(Interlocked.Exchange(ref _pendingFixedUpdates, 0), MAX_FIXED_UPDATES_PER_FRAME);
                Time.FixedDeltaTime = ProjectSettings.PhysicsTimestep;
                for (int step = 0; step < fixedUpdates; step++)
                {
                    _useClassesQueue = true;
                    foreach (IGameClass gameClass in _classes)
                    {
                        if (_classesRemoveQueue.Contains(gameClass)) continue;
                        gameClass.FixedUpdate();
                    }
                    _useClassesQueue = false;
                    PopClassesQueue();
                }
                PerformanceTimings.PhysicsMilliseconds = (Glfw.Time - phyST) * 1000;
                // -------------------------------


                // Rendering
                double rendST = Glfw.Time;
                ApplyBlendingMode();
                GLClear();
                double ppST = Glfw.Time;
                PostProcessor.Instance.BeforeGameRender();
                RenderCall();
                PostProcessor.Instance.AfterGameRender();
                PostProcessor.Instance.Render();
                PerformanceTimings.PostProcessingMilliseconds = (Glfw.Time - ppST) * 1000;
                RenderLayerManager.RenderAllLayersIgnorePostProcessing();

                Glfw.SwapBuffers(Display.Window);
                if (ProjectSettings.GraphicsErrorCheckingEnabled) LogErrors();
                PerformanceTimings.RenderMilliseconds = (Glfw.Time - rendST) * 1000;
                // -------------------------------
            }

            Shutdown(false);
        }

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint period);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint period);

        private void RunPhysicsThread(CancellationToken token, float physicsTimestep, int velocityIterations, int positionIterations)
        {
            // Lets Thread.Sleep(1) sleep for about 1ms instead of about 15ms on Windows
            bool raisedTimerResolution = OperatingSystem.IsWindows() && timeBeginPeriod(1) == 0;
            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                double nextStepTime = physicsTimestep;
                while (!token.IsCancellationRequested)
                {
                    double time = stopwatch.Elapsed.TotalSeconds;
                    if (time < nextStepTime)
                    {
                        // Sleep while there's time to spare instead of spinning the CPU
                        if (nextStepTime - time > 0.002) Thread.Sleep(1);
                        else Thread.Yield();
                        continue;
                    }

                    int steps = 0;
                    while (time >= nextStepTime && steps < MAX_PHYSICS_STEPS_PER_TICK)
                    {
                        try
                        {
                            Physics.Step(physicsTimestep, velocityIterations, positionIterations);
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogError($"PHYSICS: Step failed. {e}");
                        }
                        Interlocked.Increment(ref _pendingFixedUpdates);
                        nextStepTime += physicsTimestep;
                        steps++;
                    }

                    // Too far behind, so skip the missed time
                    if (time >= nextStepTime) nextStepTime = time + physicsTimestep;
                }
            }
            finally
            {
                if (raisedTimerResolution) timeEndPeriod(1);
            }
        }

        private void GLClear()
        {
            glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT | GL_STENCIL_BUFFER_BIT);
            glClearColor(LinearBackgroundColor.X, LinearBackgroundColor.Y, LinearBackgroundColor.Z, 1);
        }

        private void RenderCall()
        {
            Render();
            RenderLayerManager.RenderAllLayers();
        }

        private void LogErrors()
        {
            int errorCode = GetError();
            if (errorCode != GL_NO_ERROR)
                Debug.LogError($"OPENGL {errorCode}");

            string description;
            ErrorCode code = Glfw.GetError(out description);
            if (code != ErrorCode.None)
            {
                Debug.LogError($"GLFW {description} | {code}");
            }
            while (code != ErrorCode.None)
            {
                code = Glfw.GetError(out description);
                Debug.LogError($"GLFW {description} | {code}");
            }
        }

        /// <summary>
        /// Exits the game.
        /// </summary>
        public void Exit()
        {
            if (_hasExited) return;
            _exitRequested = true;
            if (Display.Window != Window.None)
                Glfw.SetWindowShouldClose(Display.Window, true);
        }

        private void Shutdown(bool terminateGlfw = true)
        {
            if (_hasExited) return;
            _hasExited = true;

            ShutdownStep(OnGameClose);
            List<IGameClass> classesCopy = new List<IGameClass>(_classes);

            // Network goes first, so disconnect events run while the rest of the game still exists
            foreach (IGameClass gameClass in classesCopy)
            {
                if (gameClass is Network) ShutdownStep(gameClass.Dispose);
            }
            foreach (IGameClass gameClass in classesCopy)
            {
                if (gameClass is not Network) ShutdownStep(gameClass.Dispose);
            }
            ShutdownStep(() =>
            {
                PhysicsCancellationToken.Cancel();
                if (PhysicsThread != null && PhysicsThread.ThreadState != ThreadState.Unstarted)
                    PhysicsThread.Join();
                PhysicsCancellationToken.Dispose();
            });
            ShutdownStep(Material.Shutdown);
            ShutdownStep(GlobalShaders.Shutdown);
            ShutdownStep(AudioSystem.Shutdown);
            ShutdownStep(Resources.Shutdown);
            if (terminateGlfw)
            {
                ShutdownStep(Display.DestroyWindow);
                ShutdownStep(Glfw.Terminate);
            }
            Debug.CloseLogFile();
        }

        private static void ShutdownStep(Action step)
        {
            try
            {
                step();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error during shutdown: {e}");
            }
        }

        private void Crash(System.Exception e)
        {
            string message = e is FatalException ? e.Message : $"An unexpected error occurred.\n\n{e.GetType().Name}: {e.Message}";
            Debug.LogError($"FATAL: {e}");
            Shutdown();
            FatalErrorDialog.Show(message);
            Environment.ExitCode = 1;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            System.Exception e = args.ExceptionObject as System.Exception;
            Debug.LogError($"FATAL: Unhandled exception on thread {Thread.CurrentThread.Name ?? Thread.CurrentThread.ManagedThreadId.ToString()}: {args.ExceptionObject}");
            Debug.CloseLogFile();
            FatalErrorDialog.Show(e is FatalException ? e.Message : $"An unexpected error occurred.\n\n{e?.GetType().Name}: {e?.Message}");
        }

        protected virtual void Initialize() { }      // This is ran when the game is first initialized
        protected virtual void Load() { }            // This is ran when the game is ready to load content
        protected virtual void Update() { }          // This is ran every frame
        protected virtual void Render() { }          // This is ran every frame right before rendering
        protected virtual void OnGameClose() { }     // This is ran when the game is closing
    }
}