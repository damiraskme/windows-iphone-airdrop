using Microsoft.Win32;

namespace BlueDrop
{
    public static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsEnabled
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(App.AppName) is not null;
            }
        }

        public static void Set(bool enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);

            if (enabled)
            {
                key.SetValue(App.AppName, $"\"{Environment.ProcessPath}\"");
            }
            else
            {
                key.DeleteValue(App.AppName, false);
            }
        }
    }
}
