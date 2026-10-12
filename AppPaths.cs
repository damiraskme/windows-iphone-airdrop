using System.IO;

namespace ClipboardPhonePc
{
    public static class AppPaths
    {
        public static string DataDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            App.AppName);

        public static string DevicesFile => Path.Combine(DataDirectory, "devices.json");

        public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

        public static string CertificateFile => Path.Combine(DataDirectory, "certificate.cer");

        public static string IssuerFile => Path.Combine(DataDirectory, "issuer.cer");

        public static string LogFile => Path.Combine(DataDirectory, "log.txt");

        public static string PreviousLogFile => Path.Combine(DataDirectory, "log.old.txt");

        public static string DefaultSaveDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            App.AppName);

        public static string TempDirectory => Path.Combine(Path.GetTempPath(), App.AppName);

        public static string CreateTempItemDirectory()
        {
            var directory = Path.Combine(TempDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        public static void PruneTempDirectory(TimeSpan lifetime)
        {
            if (!Directory.Exists(TempDirectory))
            {
                return;
            }

            foreach (var directory in Directory.EnumerateDirectories(TempDirectory))
            {
                try
                {
                    if (DateTime.UtcNow - Directory.GetCreationTimeUtc(directory) > lifetime)
                    {
                        Directory.Delete(directory, true);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        public static void ClearTempDirectory()
        {
            try
            {
                if (Directory.Exists(TempDirectory))
                {
                    Directory.Delete(TempDirectory, true);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
