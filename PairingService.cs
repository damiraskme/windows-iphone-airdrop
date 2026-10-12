using System.Security.Cryptography;
using System.Text;

namespace ClipboardPhonePc
{
    public sealed class PairingService
    {
        private const int CodeRange = 1_000_000;
        private const string CodeFormat = "D6";
        private const int MaxAttempts = 3;

        public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(2);

        private readonly object _sync = new();
        private readonly DeviceStore _devices;
        private string? _code;
        private DateTime _expiresAt;
        private int _attemptsLeft;

        public PairingService(DeviceStore devices)
        {
            _devices = devices;
        }

        public event Action? Changed;

        public event Action<PairedDevice>? Paired;

        public string? ActiveCode
        {
            get
            {
                lock (_sync)
                {
                    return _code is not null && DateTime.UtcNow < _expiresAt ? _code : null;
                }
            }
        }

        public void BeginPairing()
        {
            lock (_sync)
            {
                _code = RandomNumberGenerator.GetInt32(CodeRange).ToString(CodeFormat);
                _expiresAt = DateTime.UtcNow + CodeLifetime;
                _attemptsLeft = MaxAttempts;
            }

            AppLog.Info("Pairing code shown");
            Changed?.Invoke();
        }

        public string? CompletePairing(string code, string deviceName, string source)
        {
            bool matched;
            int attemptsLeft;

            lock (_sync)
            {
                if (_code is null || DateTime.UtcNow >= _expiresAt)
                {
                    AppLog.Warn($"Pairing attempt from {source} with no active code");
                    return null;
                }

                matched = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(_code), Encoding.UTF8.GetBytes(code));

                if (matched || --_attemptsLeft <= 0)
                {
                    _code = null;
                }

                attemptsLeft = _attemptsLeft;
            }

            if (!matched)
            {
                AppLog.Warn(attemptsLeft > 0
                    ? $"Wrong pairing code from {source}, attempts left: {attemptsLeft}"
                    : $"Wrong pairing code from {source}, code cancelled");

                if (attemptsLeft <= 0)
                {
                    Changed?.Invoke();
                }

                return null;
            }

            var key = _devices.Add(deviceName, out var device);
            AppLog.Info($"Paired \"{device.Name}\" ({device.Id[..DeviceStore.ShortIdLength]}) from {source}");
            Changed?.Invoke();
            Paired?.Invoke(device);
            return key;
        }
    }
}
