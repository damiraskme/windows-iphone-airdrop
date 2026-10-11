using System.Buffers.Text;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BlueDrop
{
    public sealed record PairedDevice(string Id, string Name, string KeyHash, DateTime PairedAt, DateTime? LastUsedAt = null);

    public sealed class DeviceStore
    {
        private const int KeyBytes = 32;
        private static readonly TimeSpan LastUsedSaveInterval = TimeSpan.FromMinutes(1);
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly object _sync = new();
        private readonly List<PairedDevice> _devices;
        private DateTime _lastUsedSavedAt;

        public DeviceStore()
        {
            _devices = Load();
        }

        public event Action? Changed;

        public string Add(string name)
        {
            var key = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(KeyBytes));

            lock (_sync)
            {
                _devices.Add(new PairedDevice(Guid.NewGuid().ToString("N"), UniqueName(name), Convert.ToBase64String(Hash(key)), DateTime.UtcNow));
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

                _devices[index] = _devices[index] with { Name = name };
                Save();
            }
        }

        public void Remove(string id)
        {
            lock (_sync)
            {
                _devices.RemoveAll(device => device.Id == id);
                Save();
            }

            Changed?.Invoke();
        }

        public bool Authorize(string key)
        {
            var hash = Hash(key);

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
                    return false;
                }

                var now = DateTime.UtcNow;
                var isFirstUse = _devices[index].LastUsedAt is null;
                _devices[index] = _devices[index] with { LastUsedAt = now };

                if (isFirstUse || now - _lastUsedSavedAt >= LastUsedSaveInterval)
                {
                    Save();
                    _lastUsedSavedAt = now;
                }

                return true;
            }
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
            if (!File.Exists(AppPaths.DevicesFile))
            {
                return [];
            }

            var json = File.ReadAllText(AppPaths.DevicesFile);
            return string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<PairedDevice>>(json) ?? [];
        }

        private void Save()
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(AppPaths.DevicesFile, JsonSerializer.Serialize(_devices, JsonOptions));
        }
    }
}
