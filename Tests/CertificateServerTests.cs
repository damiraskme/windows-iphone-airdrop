using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using Xunit;

namespace ClipboardPhonePc.Tests
{
    public sealed class CertificateServerTests : TestEnvironment
    {
        private const string KeyName = "ClipboardPhonePc Tests Download";

        private readonly CertificateAuthority _authority;

        public CertificateServerTests()
        {
            _authority = new CertificateAuthority(KeyName);
        }

        private static int FreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        private static string Token(string page)
        {
            return System.Text.RegularExpressions.Regex.Match(page, "decide\\?t=([0-9a-f]{32})").Groups[1].Value;
        }

        private static async Task<byte[]> DownloadAsync(CertificateServer server)
        {
            using var client = new HttpClient();
            return await client.GetByteArrayAsync($"http://localhost:{server.Port}/certificate.mobileconfig");
        }

        public override void Dispose()
        {
            if (CngKey.Exists(KeyName))
            {
                using var key = CngKey.Open(KeyName);
                key.Delete();
            }

            base.Dispose();
        }

        [Fact]
        public async Task UsesPreferredPortWhenFree()
        {
            var port = FreePort();
            var server = new CertificateServer(_authority);

            Assert.True(await server.StartAsync(port));
            Assert.Equal(port, server.Port);
            Assert.Equal(_authority.CreateProfile(), await DownloadAsync(server));

            await server.StopAsync();
        }

        [Fact]
        public async Task FindsAnotherPortWhenPreferredIsTaken()
        {
            using var blocker = new TcpListener(IPAddress.Any, 0) { ExclusiveAddressUse = true };
            blocker.Start();
            var taken = ((IPEndPoint)blocker.LocalEndpoint).Port;
            var server = new CertificateServer(_authority);

            Assert.True(await server.StartAsync(taken));
            Assert.NotEqual(taken, server.Port);
            Assert.Equal(_authority.CreateProfile(), await DownloadAsync(server));

            await server.StopAsync();
        }

        [Fact]
        public async Task NameThatWorksKeepsCertificate()
        {
            var server = new CertificateServer(_authority, TimeSpan.FromSeconds(5));
            var results = new List<bool>();
            server.LocalNameChecked += results.Add;
            await server.StartAsync(FreePort());
            using var client = new HttpClient();

            var token = Token(await client.GetStringAsync($"http://localhost:{server.Port}/"));
            await client.GetAsync($"http://localhost:{server.Port}/probe?t={token}");
            await client.GetAsync($"http://localhost:{server.Port}/decide?t={token}");

            Assert.Equal([true], results);
            Assert.False(_authority.AllowsAddresses);
            await server.StopAsync();
        }

        [Fact]
        public async Task NameThatFailsSwitchesToAddresses()
        {
            var original = _authority.Fingerprint;
            var server = new CertificateServer(_authority, TimeSpan.FromMilliseconds(200));
            var results = new List<bool>();
            server.LocalNameChecked += results.Add;
            await server.StartAsync(FreePort());
            using var client = new HttpClient();

            var token = Token(await client.GetStringAsync($"http://localhost:{server.Port}/"));
            await client.GetAsync($"http://localhost:{server.Port}/decide?t={token}");
            var profile = await DownloadAsync(server);

            Assert.Equal([false], results);
            Assert.True(_authority.AllowsAddresses);
            Assert.NotEqual(original, _authority.Fingerprint);
            Assert.Equal(_authority.CreateProfile(), profile);
            await server.StopAsync();
        }

        [Fact]
        public async Task UnknownTokenIsRefused()
        {
            var server = new CertificateServer(_authority);
            await server.StartAsync(FreePort());
            using var client = new HttpClient();

            var response = await client.GetAsync($"http://localhost:{server.Port}/decide?t=nope");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.False(_authority.AllowsAddresses);
            await server.StopAsync();
        }

        [Fact]
        public async Task StopsListening()
        {
            var server = new CertificateServer(_authority);
            await server.StartAsync(FreePort());
            await server.StopAsync();

            await Assert.ThrowsAsync<HttpRequestException>(() => DownloadAsync(server));
        }

        [Fact]
        public async Task OtherPathsAreNotServed()
        {
            var server = new CertificateServer(_authority);
            await server.StartAsync(FreePort());
            using var client = new HttpClient();

            var response = await client.GetAsync($"http://localhost:{server.Port}/ping");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            await server.StopAsync();
        }
    }
}
