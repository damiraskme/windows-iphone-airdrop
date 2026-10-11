using System.IO;
using System.Text.Json;

namespace BlueDrop
{
    public sealed class AppSettings
    {
        public const int MinPopupSeconds = 1;
        public const int MaxPopupSeconds = 60;
        public const int MinPort = 1024;
        public const int MaxPort = 65535;

        private const int DefaultHotkeyModifiers = 3;
        private const int DefaultHotkeyKey = 0x56;

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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

        public static AppSettings Load()
        {
            if (!File.Exists(AppPaths.SettingsFile))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(AppPaths.SettingsFile);
            var settings = string.IsNullOrWhiteSpace(json) ? new AppSettings() : JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

            settings.PopupSeconds = Math.Clamp(settings.PopupSeconds, MinPopupSeconds, MaxPopupSeconds);
            settings.Port = Math.Clamp(settings.Port, MinPort, MaxPort);
            return settings;
        }

        public void Save()
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, JsonOptions));
        }
    }
}
