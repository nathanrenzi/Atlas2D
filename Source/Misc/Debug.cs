using GLFW;

namespace Atlas2D
{
    public static class Debug
    {
        public static bool DebuggingEnabled = true;
        public static bool EnableLogMessages = true;
        public static bool EnableErrorMessages = true;
        public static bool EnableWarningMessages = true;
        public static bool EnableCollapsing = true;

        private static string _lastMessage = "";
        private static int _messageCount = 1;
        private static bool _hasLoggedMessage = false;
        private static StreamWriter _logFile = null;
        private static readonly object _writeLock = new object();

        /// <summary>
        /// The full path of the current log file.
        /// </summary>
        public static string LogFilePath { get; private set; } = "";

        /// <summary>
        /// Used to log formatted and togglable messages to the console and the latest log file.
        /// </summary>
        /// <param name="_message"></param>
        public static void Log(object message)
        {
            Log(message, ConsoleColor.White);
        }

        /// <summary>
        /// Used to log formatted and togglable messages to the console and the latest log file.
        /// </summary>
        /// <param name="_message"></param>
        public static void Log(object message, ConsoleColor messageColor)
        {
            if (!DebuggingEnabled || !EnableLogMessages) return;
            Write($"{message}", messageColor);
        }

        /// <summary>
        /// Used to log formatted and togglable error messages to the console and the latest log file.
        /// </summary>
        /// <param name="message"></param>
        public static void LogError(object message)
        {
            if (!DebuggingEnabled || !EnableErrorMessages) return;
            Write($"ERROR: {message}", ConsoleColor.Red);
        }

        /// <summary>
        /// Used to log formatted and togglable warning messages to the console and the latest log file.
        /// </summary>
        /// <param name="message"></param>
        public static void LogWarning(object message)
        {
            if (!DebuggingEnabled || !EnableWarningMessages) return;
            Write($"WARNING: {message}", ConsoleColor.Yellow);
        }

        private static void Write(string message, ConsoleColor color)
        {
            lock (_writeLock)
            {
                Console.ForegroundColor = color;
                WriteUnlocked(message);
                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        private static void WriteUnlocked(string message)
        {
            if (_lastMessage.Equals(message) && EnableCollapsing)
            {
                _messageCount++;

                string m = $"\r[{GetTimeString()}] {message} (x{_messageCount})";
                Console.Write(m);
                _logFile?.Write(m);
            }
            else
            {
                _messageCount = 1;

                if (_hasLoggedMessage)
                {
                    Console.WriteLine();
                    _logFile?.WriteLine();
                }

                string m = $"[{GetTimeString()}] {message}";
                Console.Write(m);
                _logFile?.Write(m);
            }

            _lastMessage = message;
            _hasLoggedMessage = true;
        }

        private static string GetTimeString()
        {
            return TimeSpan.FromSeconds(Glfw.Time).ToString();
        }

        public static void OpenLogFile()
        {
            if(!Directory.Exists("Logs"))
            {
                Directory.CreateDirectory("Logs");
            }
            // Another running copy of the game keeps its log open, so later copies use the next free name
            for (int i = 1; _logFile == null; i++)
            {
                string path = Path.GetFullPath(i == 1 ? "Logs/latest.txt" : $"Logs/latest ({i}).txt");
                try
                {
                    _logFile = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
                    LogFilePath = path;
                }
                catch (IOException) when (i < 10) { }
            }
        }

        public static void CloseLogFile()
        {
            lock (_writeLock)
            {
                _logFile?.Close();
                _logFile = null;
            }
        }
    }
}
