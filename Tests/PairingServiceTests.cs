using Xunit;

namespace ClipboardPhonePc.Tests
{
    public sealed class PairingServiceTests : TestEnvironment
    {
        private const string Source = "test";

        private readonly DeviceStore _devices;

        public PairingServiceTests()
        {
            _devices = new DeviceStore();
        }

        private static string WrongCode(string code)
        {
            return code == "000000" ? "000001" : "000000";
        }

        [Fact]
        public async Task NothingPairsWithoutCode()
        {
            var pairing = new PairingService(_devices);

            Assert.Null(await pairing.CompletePairingAsync("123456", "Phone", Source));
            Assert.Empty(_devices.GetAll());
        }

        [Fact]
        public async Task RightCodePairsOnce()
        {
            var pairing = new PairingService(_devices);
            pairing.BeginPairing();
            var code = pairing.ActiveCode!;

            var key = await pairing.CompletePairingAsync(code, "Phone", Source);

            Assert.NotNull(key);
            Assert.NotNull(_devices.Authorize(key));
            Assert.Null(pairing.ActiveCode);
            Assert.Null(await pairing.CompletePairingAsync(code, "Phone", Source));
        }

        [Fact]
        public async Task TwoWrongAttemptsKeepCode()
        {
            var pairing = new PairingService(_devices);
            pairing.BeginPairing();
            var code = pairing.ActiveCode!;

            Assert.Null(await pairing.CompletePairingAsync(WrongCode(code), "Phone", Source));
            Assert.Null(await pairing.CompletePairingAsync(WrongCode(code), "Phone", Source));

            Assert.NotNull(await pairing.CompletePairingAsync(code, "Phone", Source));
        }

        [Fact]
        public async Task ThreeWrongAttemptsCancelCode()
        {
            var pairing = new PairingService(_devices);
            pairing.BeginPairing();
            var code = pairing.ActiveCode!;

            for (var attempt = 0; attempt < 3; attempt++)
            {
                Assert.Null(await pairing.CompletePairingAsync(WrongCode(code), "Phone", Source));
            }

            Assert.Null(pairing.ActiveCode);
            Assert.Null(await pairing.CompletePairingAsync(code, "Phone", Source));
        }

        [Fact]
        public async Task DeclinedPairingAddsNoDevice()
        {
            var asked = string.Empty;
            var pairing = new PairingService(_devices)
            {
                Confirm = (name, source) =>
                {
                    asked = $"{name} {source}";
                    return Task.FromResult(false);
                }
            };
            pairing.BeginPairing();

            Assert.Null(await pairing.CompletePairingAsync(pairing.ActiveCode!, "Phone", Source));
            Assert.Equal("Phone test", asked);
            Assert.Empty(_devices.GetAll());
            Assert.Null(pairing.ActiveCode);
        }

        [Fact]
        public async Task WrongCodeDoesNotAsk()
        {
            var asked = false;
            var pairing = new PairingService(_devices)
            {
                Confirm = (_, _) =>
                {
                    asked = true;
                    return Task.FromResult(true);
                }
            };
            pairing.BeginPairing();

            await pairing.CompletePairingAsync(WrongCode(pairing.ActiveCode!), "Phone", Source);

            Assert.False(asked);
        }
    }
}
