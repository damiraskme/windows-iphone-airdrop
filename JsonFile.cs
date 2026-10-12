using System.IO;
using System.Text.Json;

namespace ClipboardPhonePc
{
    public enum JsonFileState
    {
        Missing,
        Loaded,
        Restored,
        Damaged
    }

    public static class JsonFile
    {
        private const string BackupExtension = ".bak";
        private const string TempExtension = ".tmp";
        private const string DamagedExtension = ".bad";

        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

        public static T? Load<T>(string path, out JsonFileState state) where T : class
        {
            if (!File.Exists(path) && !File.Exists(path + BackupExtension))
            {
                state = JsonFileState.Missing;
                return null;
            }

            if (TryRead<T>(path) is { } value)
            {
                state = JsonFileState.Loaded;
                return value;
            }

            if (File.Exists(path))
            {
                File.Move(path, path + DamagedExtension, true);
            }

            if (TryRead<T>(path + BackupExtension) is { } backup)
            {
                File.Copy(path + BackupExtension, path, true);
                AppLog.Warn($"\"{Path.GetFileName(path)}\" was damaged, restored from backup");
                state = JsonFileState.Restored;
                return backup;
            }

            AppLog.Error($"\"{Path.GetFileName(path)}\" was damaged and no backup could be read");
            state = JsonFileState.Damaged;
            return null;
        }

        public static void Save<T>(string path, T value, bool refreshBackup = false)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var temp = path + TempExtension;
            File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));

            if (File.Exists(path))
            {
                File.Replace(temp, path, path + BackupExtension);
            }
            else
            {
                File.Move(temp, path);
            }

            if (refreshBackup)
            {
                File.Copy(path, path + BackupExtension, true);
            }
        }

        private static T? TryRead<T>(string path) where T : class
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(path);
                return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
