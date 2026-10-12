namespace ClipboardPhonePc
{
    public sealed class AppSettings
    {
        public const int MinPopupSeconds = 1;
        public const int MaxPopupSeconds = 60;
        public const int MinPort = 1024;
        public const int MaxPort = 65535;

        private const int DefaultHotkeyModifiers = 3;
        private const int DefaultHotkeyKey = 0x56;

        public bool PopupEnabled { get; set; } = true;

        public bool HidePopupContent { get; set; }

        public int PopupSeconds { get; set; } = 5;

        public bool AutoCopy { get; set; }

        public bool SoundEnabled { get; set; }

        public bool SaveFiles { get; set; }

        public string? SaveFolder { get; set; }

        public bool ClearAfterFetch { get; set; }

        public int HotkeyModifiers { get; set; } = DefaultHotkeyModifiers;

        public int HotkeyKey { get; set; } = DefaultHotkeyKey;

        public int Port { get; set; } = 5055;

        public bool AllowNetwork { get; set; }

        public int MaxFileMegabytes { get; set; } = 8192;

        public static AppSettings Load()
        {
            var settings = JsonFile.Load<AppSettings>(AppPaths.SettingsFile, out var state) ?? new AppSettings();

            if (state == JsonFileState.Restored)
            {
                AppStatus.Report(StatusIssue.Settings, AppStatus.SettingsRestored);
            }
            else if (state == JsonFileState.Damaged)
            {
                AppStatus.Report(StatusIssue.Settings, AppStatus.SettingsReset);
            }

            settings.PopupSeconds = Math.Clamp(settings.PopupSeconds, MinPopupSeconds, MaxPopupSeconds);
            settings.Port = Math.Clamp(settings.Port, MinPort, MaxPort);
            settings.MaxFileMegabytes = Math.Max(settings.MaxFileMegabytes, 1);
            return settings;
        }

        public void Save()
        {
            JsonFile.Save(AppPaths.SettingsFile, this);
        }
    }
}
