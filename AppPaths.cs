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

        public static string InboxDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            App.AppName);
    }
}
