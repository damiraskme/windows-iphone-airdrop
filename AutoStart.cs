using System.IO;
using Microsoft.Win32;

namespace ClipboardPhonePc
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

        public static void Repair()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);

            if (key?.GetValue(App.AppName) is string registered
                && Environment.ProcessPath is { } path
                && !File.Exists(registered.Trim('"')))
            {
                key.SetValue(App.AppName, $"\"{path}\"");
                AppLog.Info($"Start with Windows now points to \"{path}\"");
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
