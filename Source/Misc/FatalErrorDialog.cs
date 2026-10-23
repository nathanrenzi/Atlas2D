using System.Runtime.InteropServices;

namespace Atlas2D
{
    internal static class FatalErrorDialog
    {
        private const uint MB_OK = 0x0;
        private const uint MB_ICONERROR = 0x10;
        private const uint MB_SETFOREGROUND = 0x10000;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        public static void Show(string message)
        {
            string text = string.IsNullOrEmpty(Debug.LogFilePath)
                ? message
                : $"{message}\n\nDetails were saved to:\n{Debug.LogFilePath}";

            try
            {
                MessageBoxW(IntPtr.Zero, text, ProjectSettings.WindowTitle, MB_OK | MB_ICONERROR | MB_SETFOREGROUND);
            }
            catch
            {
                Console.WriteLine(text);
            }
        }
    }
}
