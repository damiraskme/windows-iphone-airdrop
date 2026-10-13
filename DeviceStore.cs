using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace ClipboardPhonePc
{
    public sealed record PairedDevice(string Id, string Name, string KeyHash, DateTime PairedAt, DateTime? LastUsedAt = null);

    public sealed class DeviceStore
    {
        public const int ShortIdLength = 8;

        private const int KeyBytes = 32;
        private static readonly TimeSpan LastUsedSaveInterval = TimeSpan.FromMinutes(1);

        private readonly object _sync = new();
        private readonly List<PairedDevice> _devices;
        private DateTime _lastUsedSavedAt;

        public DeviceStore()
        {
            _devices = Load();
        }

        public event Action? Changed;

        public string Add(string name, out PairedDevice device)
        {
            var key = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(KeyBytes));

            lock (_sync)
            {
                device = new PairedDevice(Guid.NewGuid().ToString("N"), UniqueName(name), Convert.ToBase64String(Hash(key)), DateTime.UtcNow);
                _devices.Add(device);
                Save();
            }

            Changed?.Invoke();
            return key;
        }

        public IReadOnlyList<PairedDevice> GetAll()
        {
            lock (_sync)
            {
                return [.. _devices];
            }
        }

        public void Rename(string id, string name)
        {
            lock (_sync)
            {
                var index = _devices.FindIndex(device => device.Id == id);
                if (index < 0)
                {
                    return;
                }

                AppLog.Info($"Renamed device \"{_devices[index].Name}\" ({id[..ShortIdLength]}) to \"{name}\"");
                _devices[index] = _devices[index] with { Name = name };
                Save();
            }
        }

        public void Remove(string id)
        {
            lock (_sync)
            {
                foreach (var device in _devices.Where(device => device.Id == id))
                {
                    AppLog.Info($"Removed device \"{device.Name}\" ({id[..ShortIdLength]})");
                }

                _devices.RemoveAll(device => device.Id == id);
                Save(true);
            }

            Changed?.Invoke();
        }

        public PairedDevice? Authorize(string key)
        {
            var hash = Hash(key);
            PairedDevice device;
            bool isFirstUse;

            lock (_sync)
            {
                var index = -1;
                for (var i = 0; i < _devices.Count; i++)
                {
                    if (CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(_devices[i].KeyHash)))
                    {
                        index = i;
                    }
                }

                if (index < 0)
                {
                    return null;
                }

                var now = DateTime.UtcNow;
                isFirstUse = _devices[index].LastUsedAt is null;
                _devices[index] = _devices[index] with { LastUsedAt = now };

                if (isFirstUse || now - _lastUsedSavedAt >= LastUsedSaveInterval)
                {
                    Save();
                    _lastUsedSavedAt = now;
                }

                device = _devices[index];
            }

            if (isFirstUse)
            {
                Changed?.Invoke();
            }

            return device;
        }

        private string UniqueName(string name)
        {
            if (name.Length == 0)
            {
                return name;
            }

            var unique = name;

            for (var index = 1; _devices.Any(device => string.Equals(device.Name, unique, StringComparison.OrdinalIgnoreCase)); index++)
            {
                unique = $"{name} ({index})";
            }

            return unique;
        }

        private static byte[] Hash(string key)
        {
            return SHA256.HashData(Encoding.UTF8.GetBytes(key));
        }

        private static List<PairedDevice> Load()
        {
            var devices = JsonFile.Load<List<PairedDevice>>(AppPaths.DevicesFile, out var state) ?? [];

            if (state == JsonFileState.Restored)
            {
                AppStatus.Report(StatusIssue.Devices, AppStatus.DevicesRestored);
            }
            else if (state == JsonFileState.Damaged)
            {
                AppStatus.Report(StatusIssue.Devices, AppStatus.DevicesReset);
            }

            return devices;
        }

        private void Save(bool refreshBackup = false)
        {
            JsonFile.Save(AppPaths.DevicesFile, _devices, refreshBackup);
        }
    }
}
