using Atlas2D.Rendering;
using GLFW;
using Newtonsoft.Json;

namespace Atlas2D
{
    public class Settings
    {
        private const string FilePath = "Settings.json";
        private const string CorruptFilePath = "Settings.corrupt.json";

        public class SettingsJson
        {
            public int Monitor { get; set; }
            public int WindowWidth { get; set; }
            public int WindowHeight { get; set; }
            public WindowMode WindowMode { get; set; }
            public bool Vsync { get; set; }
            public AntialiasingMode AntialiasingMode { get; set; }
            public int RefreshRate { get; set; }
            public float AudioMasterVolume { get; set; }

            public SettingsJson()
            {
                Monitor = (int)Glfw.PrimaryMonitor.UserPointer;
                WindowHeight = 0;
                WindowWidth = 0;
                WindowMode = WindowMode.BorderlessWindow;
                Vsync = true;
                AntialiasingMode = AntialiasingMode.FXAA;
                RefreshRate = 0;
                AudioMasterVolume = 1;
            }

            public SettingsJson(int monitor, int windowWidth, int windowHeight,
                WindowMode windowMode, bool vsync, AntialiasingMode antialiasingMode,
                int refreshRate, float audioMasterVolume)
            {
                Monitor = monitor;
                WindowWidth = windowWidth;
                WindowHeight = windowHeight;
                WindowMode = windowMode;
                Vsync = vsync;
                AntialiasingMode = antialiasingMode;
                RefreshRate = refreshRate;
                AudioMasterVolume = audioMasterVolume;
            }
        }

        public GLFW.Monitor Monitor { get; set; }
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public WindowMode WindowMode { get; set; }
        public bool Vsync { get; set; }
        public AntialiasingMode AntialiasingMode { get; set; }
        public int RefreshRate { get; set; }
        public float AudioMasterVolume { get; set; }

        private Settings(SettingsJson json)
        {
            Monitor = Glfw.PrimaryMonitor;
            for (int i = 0; i < Glfw.Monitors.Length; i++)
            {
                if (Glfw.Monitors[i].UserPointer == json.Monitor)
                {
                    Monitor = Glfw.Monitors[i];
                    break;
                }
            }
            WindowWidth = json.WindowWidth;
            WindowHeight = json.WindowHeight;
            WindowMode = json.WindowMode;
            Vsync = json.Vsync;
            AntialiasingMode = json.AntialiasingMode;
            RefreshRate = json.RefreshRate;
            AudioMasterVolume = json.AudioMasterVolume;
        }

        public static Settings Load()
        {
            SettingsJson settingsJson = null;
            if (File.Exists(FilePath))
            {
                try
                {
                    settingsJson = JsonConvert.DeserializeObject<SettingsJson>(File.ReadAllText(FilePath));
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Could not read {FilePath}: {e.Message}");
                }

                if (settingsJson == null)
                {
                    Debug.LogWarning($"{FilePath} is corrupt, so default settings will be used. The old file was moved to {CorruptFilePath}");
                    File.Move(FilePath, CorruptFilePath, true);
                }
            }
            settingsJson ??= new SettingsJson();

            GLFW.Monitor monitor = GLFW.Monitor.None;
            bool foundMonitor = false;
            for (int i = 0; i < Glfw.Monitors.Length; i++)
            {
                if (Glfw.Monitors[i].UserPointer == settingsJson.Monitor)
                {
                    monitor = Glfw.Monitors[i];
                    foundMonitor = true;
                    break;
                }
            }
            if (!foundMonitor)
            {
                monitor = Glfw.PrimaryMonitor;
                settingsJson.Monitor = (int)Glfw.PrimaryMonitor.UserPointer;
            }

            if (settingsJson.WindowWidth == 0 || settingsJson.WindowHeight == 0 || settingsJson.RefreshRate == 0)
            {
                settingsJson.WindowWidth = monitor.WorkArea.Width;
                settingsJson.WindowHeight = monitor.WorkArea.Height;
                settingsJson.RefreshRate = Glfw.GetVideoMode(monitor).RefreshRate;
            }

            Settings s = new Settings(settingsJson);
            try
            {
                s.Save();
            }
            catch (IOException e)
            {
                Debug.LogWarning($"Could not save {FilePath}: {e.Message}");
            }
            return s;
        }

        public void Save()
        {
            SettingsJson settingsJson = new SettingsJson((int)Monitor.UserPointer, WindowWidth, WindowHeight, WindowMode,
                Vsync, AntialiasingMode, RefreshRate, AudioMasterVolume);
            string json = JsonConvert.SerializeObject(settingsJson, Formatting.Indented);
            string tempPath = $"{FilePath}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, FilePath, true);
        }
    }
}
