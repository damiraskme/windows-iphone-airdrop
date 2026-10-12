using System.IO;

namespace ClipboardPhonePc
{
    public static class AppLog
    {
        private const long MaxBytes = 1024 * 1024;
        private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";
        private const string InfoLevel = "INFO";
        private const string WarnLevel = "WARN";
        private const string ErrorLevel = "ERROR";

        private static readonly object Sync = new();

        public static void Info(string message)
        {
            Write(InfoLevel, message.ReplaceLineEndings(" "));
        }

        public static void Warn(string message)
        {
            Write(WarnLevel, message.ReplaceLineEndings(" "));
        }

        public static void Error(string message, Exception? exception = null)
        {
            var line = message.ReplaceLineEndings(" ");
            Write(ErrorLevel, exception is null ? line : $"{line}: {exception}");
        }

        private static void Write(string level, string message)
        {
            lock (Sync)
            {
                try
                {
                    Directory.CreateDirectory(AppPaths.DataDirectory);

                    if (File.Exists(AppPaths.LogFile) && new FileInfo(AppPaths.LogFile).Length > MaxBytes)
                    {
                        File.Move(AppPaths.LogFile, AppPaths.PreviousLogFile, true);
                    }

                    File.AppendAllText(AppPaths.LogFile, $"{DateTime.Now.ToString(TimeFormat)} {level,-5} {message}{Environment.NewLine}");
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
