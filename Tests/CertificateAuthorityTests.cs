using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace ClipboardPhonePc.Tests
{
    public sealed class CertificateAuthorityTests : TestEnvironment
    {
        private const string KeyName = "ClipboardPhonePc Tests Issuer";
        private const string SubjectAlternativeNameOid = "2.5.29.17";

        private readonly List<string> _issuers = [];

        private CertificateAuthority Create()
        {
            var authority = new CertificateAuthority(KeyName);
            Track(authority);
            return authority;
        }

        private void Track(CertificateAuthority authority)
        {
            _issuers.Add(Convert.ToHexString(SHA1.HashData(authority.IssuerBytes)));
        }

        private static string[] ServerNames(CertificateAuthority authority)
        {
            var names = authority.ServerContext.TargetCertificate.Extensions[SubjectAlternativeNameOid]!;
            return names.Format(true).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static bool Chains(X509Certificate2 certificate, CertificateAuthority authority)
        {
            using var root = X509CertificateLoader.LoadCertificate(authority.RootBytes);
            using var issuer = X509CertificateLoader.LoadCertificate(authority.IssuerBytes);
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(root);
            chain.ChainPolicy.ExtraStore.Add(issuer);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            return chain.Build(certificate);
        }

        public override void Dispose()
        {
            if (CngKey.Exists(KeyName))
            {
                using var key = CngKey.Open(KeyName);
                key.Delete();
            }

            using var store = new X509Store(StoreName.CertificateAuthority, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);

            foreach (var certificate in store.Certificates.Where(certificate => _issuers.Contains(certificate.Thumbprint)))
            {
                store.Remove(certificate);
            }

            base.Dispose();
        }

        [Fact]
        public void IssuerChainsToRoot()
        {
            var authority = Create();
            using var root = X509CertificateLoader.LoadCertificate(authority.RootBytes);
            using var issuer = X509CertificateLoader.LoadCertificate(authority.IssuerBytes);

            Assert.True(Chains(issuer, authority));
            Assert.Equal(root.Subject, issuer.Issuer);
        }

        [Fact]
        public void ServerCertificateChainsToRoot()
        {
            var authority = Create();

            Assert.True(Chains(authority.ServerContext.TargetCertificate, authority));
        }

        [Fact]
        public void NamesOnlyCertificateHasNoAddresses()
        {
            var names = ServerNames(Create());

            Assert.Contains(names, name => name.Contains(CertificateAuthority.LocalHostName));
            Assert.DoesNotContain(names, name => name.StartsWith("IP Address", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ProfileContainsRootCertificate()
        {
            var authority = Create();
            var profile = System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(authority.CreateProfile()));

            Assert.Equal(Convert.ToBase64String(authority.RootBytes), profile.Descendants("data").Single().Value);
            Assert.Equal(2, profile.Descendants("key").Count(key => key.Value == "PayloadUUID"));
        }

        [Fact]
        public void EnablingAddressesReplacesCertificateOnce()
        {
            var authority = Create();
            var original = authority.Fingerprint;

            authority.EnableAddresses();
            Track(authority);
            var replaced = authority.Fingerprint;
            authority.EnableAddresses();

            Assert.True(authority.AllowsAddresses);
            Assert.NotEqual(original, replaced);
            Assert.Equal(replaced, authority.Fingerprint);
            Assert.Contains(ServerNames(authority), name => name.StartsWith("IP Address", StringComparison.OrdinalIgnoreCase));
            Assert.True(Chains(authority.ServerContext.TargetCertificate, authority));
        }

        [Fact]
        public void AddressModeSurvivesRestart()
        {
            var first = Create();
            first.EnableAddresses();
            Track(first);

            var second = Create();

            Assert.True(second.AllowsAddresses);
            Assert.False(second.Replaced);
            Assert.Equal(first.Fingerprint, second.Fingerprint);
        }

        [Fact]
        public void CertificateIsReusedOnRestart()
        {
            var first = Create();
            var second = Create();

            Assert.False(first.Replaced);
            Assert.False(second.Replaced);
            Assert.Equal(first.Fingerprint, second.Fingerprint);
        }
    }
}
