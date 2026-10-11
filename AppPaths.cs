using System.IO;

namespace BlueDrop
{
    public static class AppPaths
    {
        public static string DataDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            App.AppName);

        public static string DevicesFile => Path.Combine(DataDirectory, "devices.json");

        public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

        public static string CertificateFile => Path.Combine(DataDirectory, "certificate.cer");

        public static string DefaultSaveDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            App.AppName);

        public static string TempDirectory => Path.Combine(Path.GetTempPath(), App.AppName);

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
