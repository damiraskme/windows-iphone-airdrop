using System.Security.Cryptography;
using System.Text;

namespace BlueDrop
{
    public sealed class PairingService
    {
        private const int CodeRange = 1_000_000;
        private const string CodeFormat = "D6";

        public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(2);

        private readonly object _sync = new();
        private readonly DeviceStore _devices;
        private string? _code;
        private DateTime _expiresAt;

        public PairingService(DeviceStore devices)
        {
            _devices = devices;
        }

        public event Action? Changed;

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
            }

            Changed?.Invoke();
        }

        public string? CompletePairing(string code, string deviceName)
        {
            bool matched;

            lock (_sync)
            {
                matched = _code is not null
                    && DateTime.UtcNow < _expiresAt
                    && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(_code), Encoding.UTF8.GetBytes(code));
                _code = null;
            }

            Changed?.Invoke();
            return matched ? _devices.Add(deviceName) : null;
        }
    }
}
