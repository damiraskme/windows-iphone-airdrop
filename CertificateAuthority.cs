using System.Formats.Asn1;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ClipboardPhonePc
{
    public sealed class CertificateAuthority
    {
        private const string NameConstraintsOid = "2.5.29.30";
        private const string ServerAuthOid = "1.3.6.1.5.5.7.3.1";
        private const string IssuerSuffix = " Issuer";
        private const string LegacyKeySuffix = " Root";
        private const string LocalSuffix = ".local";
        private const string Localhost = "localhost";
        private const int RootYears = 20;
        private const int ServerDays = 365;
        private const int SerialLength = 16;
        private const int FingerprintGroupLength = 2;
        private const int ProfileVersion = 1;
        private const int GuidLength = 16;
        private const string ProfileDescription = "Lets this device connect securely to {0} on {1}. Valid only for that PC on the local network.";
        private const string RootPayloadType = "com.apple.security.root";
        private const string ProfilePayloadType = "Configuration";
        private const string RootIdentifierSuffix = ".root";
        private const string CertificateExtension = ".cer";
        private const string PlistName = "plist";
        private const string PlistPublicId = "-//Apple//DTD PLIST 1.0//EN";
        private const string PlistSystemId = "http://www.apple.com/DTDs/PropertyList-1.0.dtd";
        private const string PlistVersion = "1.0";

        private static readonly TimeSpan RenewBefore = TimeSpan.FromDays(30);
        private static readonly TimeSpan ClockSkew = TimeSpan.FromDays(1);

        private static readonly (IPAddress Address, int PrefixLength)[] PermittedNetworks =
        {
            (IPAddress.Parse("10.0.0.0"), 8),
            (IPAddress.Parse("172.16.0.0"), 12),
            (IPAddress.Parse("192.168.0.0"), 16),
            (IPAddress.Parse("169.254.0.0"), 16),
            (IPAddress.Parse("100.64.0.0"), 10),
            (IPAddress.Parse("127.0.0.0"), 8)
        };

        private readonly object _sync = new();
        private readonly X509Certificate2 _root;
        private readonly X509Certificate2 _issuer;
        private readonly ECDsa _issuerKey;
        private SslStreamCertificateContext? _server;
        private DateTime _serverExpiresAt;
        private string _serverAddresses = string.Empty;
        private volatile bool _serverStale;

        public CertificateAuthority()
        {
            var existing = Load();
            Replaced = existing is null && File.Exists(AppPaths.CertificateFile);
            (_root, _issuer, _issuerKey) = existing ?? Create();

            if (Replaced)
            {
                AppStatus.Report(StatusIssue.Certificate, AppStatus.CertificateReplaced);
            }

            NetworkChange.NetworkAddressChanged += (_, _) => _serverStale = true;
        }

        public static string HostName { get; } = Dns.GetHostName().ToLowerInvariant();

        public static string LocalHostName => HostName + LocalSuffix;

        public bool Replaced { get; }

        public byte[] RootBytes => _root.RawData;

        public string Fingerprint => string.Join(' ', _root.GetCertHashString(HashAlgorithmName.SHA256)
            .Chunk(FingerprintGroupLength)
            .Select(group => new string(group)));

        public SslStreamCertificateContext ServerContext
        {
            get
            {
                lock (_sync)
                {
                    if (_server is null || DateTime.Now > _serverExpiresAt - RenewBefore)
                    {
                        IssueServer(GetAddresses());
                    }
                    else if (_serverStale)
                    {
                        var addresses = GetAddresses();
                        if (FormatAddresses(addresses) != _serverAddresses)
                        {
                            IssueServer(addresses);
                        }
                    }

                    _serverStale = false;
                    return _server!;
                }
            }
        }

        public byte[] CreateProfile()
        {
            var hash = _root.GetCertHash(HashAlgorithmName.SHA256);
            var identifier = $"{App.AppName}.{HostName}".ToLowerInvariant();
            var description = string.Format(ProfileDescription, App.AppName, Environment.MachineName);

            var payload = new XElement("dict",
                Entry("PayloadCertificateFileName", App.AppName + CertificateExtension),
                new XElement("key", "PayloadContent"),
                new XElement("data", Convert.ToBase64String(_root.RawData)),
                Entry("PayloadDescription", description),
                Entry("PayloadDisplayName", RootName),
                Entry("PayloadIdentifier", identifier + RootIdentifierSuffix),
                Entry("PayloadType", RootPayloadType),
                Entry("PayloadUUID", new Guid(hash.AsSpan(0, GuidLength)).ToString().ToUpperInvariant()),
                new XElement("key", "PayloadVersion"),
                new XElement("integer", ProfileVersion));

            var profile = new XElement("dict",
                new XElement("key", "PayloadContent"),
                new XElement("array", payload),
                Entry("PayloadDescription", description),
                Entry("PayloadDisplayName", RootName),
                Entry("PayloadIdentifier", identifier),
                Entry("PayloadType", ProfilePayloadType),
                Entry("PayloadUUID", new Guid(hash.AsSpan(GuidLength, GuidLength)).ToString().ToUpperInvariant()),
                new XElement("key", "PayloadVersion"),
                new XElement("integer", ProfileVersion));

            var document = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XDocumentType(PlistName, PlistPublicId, PlistSystemId, null),
                new XElement(PlistName, new XAttribute("version", PlistVersion), profile));

            using var stream = new MemoryStream();
            using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
            {
                document.Save(writer);
            }

            return stream.ToArray();
        }

        private static object[] Entry(string key, string value)
        {
            return [new XElement("key", key), new XElement("string", value)];
        }

        private static string RootName => $"{App.AppName} {HostName}";

        private static string IssuerName => RootName + IssuerSuffix;

        private static string IssuerKeyName => App.AppName + IssuerSuffix;

        private static string[] DnsNames => [LocalHostName, HostName, Localhost];

        private static (X509Certificate2 Root, X509Certificate2 Issuer, ECDsa IssuerKey)? Load()
        {
            if (!File.Exists(AppPaths.CertificateFile) || !File.Exists(AppPaths.IssuerFile) || !CngKey.Exists(IssuerKeyName))
            {
                return null;
            }

            try
            {
                var root = X509CertificateLoader.LoadCertificateFromFile(AppPaths.CertificateFile);
                var issuer = X509CertificateLoader.LoadCertificateFromFile(AppPaths.IssuerFile);
                var key = new ECDsaCng(CngKey.Open(IssuerKeyName));
                using var publicKey = issuer.GetECDsaPublicKey();

                if (publicKey is not null
                    && root.GetNameInfo(X509NameType.SimpleName, false) == RootName
                    && issuer.GetNameInfo(X509NameType.SimpleName, false) == IssuerName
                    && issuer.NotAfter > DateTime.Now.AddDays(ServerDays)
                    && publicKey.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(key.ExportSubjectPublicKeyInfo())
                    && IsIssuedBy(issuer, root))
                {
                    AppLog.Info($"Loaded certificate {root.GetCertHashString(HashAlgorithmName.SHA256)}");
                    return (root, issuer, key);
                }

                key.Dispose();
                issuer.Dispose();
                root.Dispose();
                return null;
            }
            catch (CryptographicException e)
            {
                AppLog.Error("Could not load certificate", e);
                return null;
            }
        }

        private static (X509Certificate2 Root, X509Certificate2 Issuer, ECDsa IssuerKey) Create()
        {
            DeleteKey(IssuerKeyName);
            DeleteKey(App.AppName + LegacyKeySuffix);

            var now = DateTimeOffset.Now;
            var notBefore = now - ClockSkew;
            var notAfter = now.AddYears(RootYears);

            using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var rootName = CreateName(RootName);

            var rootRequest = new CertificateRequest(rootName, rootKey, HashAlgorithmName.SHA256);
            rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 1, true));
            rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));

            var rootSigner = X509SignatureGenerator.CreateForECDsa(rootKey);
            var root = rootRequest.Create(rootName, rootSigner, notBefore, notAfter, CreateSerial());

            var issuerKey = new ECDsaCng(CngKey.Create(CngAlgorithm.ECDsaP256, IssuerKeyName, new CngKeyCreationParameters
            {
                ExportPolicy = CngExportPolicies.None
            }));

            var issuerRequest = new CertificateRequest(CreateName(IssuerName), issuerKey, HashAlgorithmName.SHA256);
            issuerRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
            issuerRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            issuerRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(issuerRequest.PublicKey, false));
            issuerRequest.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(root, true, false));
            issuerRequest.CertificateExtensions.Add(CreateNameConstraints());

            var issuer = issuerRequest.Create(rootName, rootSigner, notBefore, notAfter, CreateSerial());

            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllBytes(AppPaths.CertificateFile, root.RawData);
            File.WriteAllBytes(AppPaths.IssuerFile, issuer.RawData);

            AppLog.Info($"Created certificate {root.GetCertHashString(HashAlgorithmName.SHA256)}");
            return (root, issuer, issuerKey);
        }

        private static bool IsIssuedBy(X509Certificate2 certificate, X509Certificate2 root)
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(root);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            return chain.Build(certificate);
        }

        private static void DeleteKey(string name)
        {
            if (CngKey.Exists(name))
            {
                using var key = CngKey.Open(name);
                key.Delete();
            }
        }

        private static X500DistinguishedName CreateName(string commonName)
        {
            var builder = new X500DistinguishedNameBuilder();
            builder.AddCommonName(commonName);
            return builder.Build();
        }

        private void IssueServer(IPAddress[] addresses)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            var names = new SubjectAlternativeNameBuilder();
            foreach (var dnsName in DnsNames)
            {
                names.AddDnsName(dnsName);
            }

            foreach (var address in addresses)
            {
                names.AddIpAddress(address);
            }

            var request = new CertificateRequest(CreateName(LocalHostName), key, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(ServerAuthOid)], false));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(_issuer, true, false));
            request.CertificateExtensions.Add(names.Build());

            var now = DateTimeOffset.Now;
            using var signed = request.Create(
                _issuer.SubjectName,
                X509SignatureGenerator.CreateForECDsa(_issuerKey),
                now - ClockSkew,
                now.AddDays(ServerDays),
                CreateSerial());
            using var exportable = signed.CopyWithPrivateKey(key);

            var certificate = X509CertificateLoader.LoadPkcs12(exportable.Export(X509ContentType.Pfx), null);

            _server = SslStreamCertificateContext.Create(certificate, [_issuer], true);
            _serverExpiresAt = certificate.NotAfter;
            _serverAddresses = FormatAddresses(addresses);

            AppLog.Info($"Issued server certificate for {string.Join(", ", DnsNames)}, {_serverAddresses}");
        }

        private static string FormatAddresses(IPAddress[] addresses)
        {
            return string.Join(", ", addresses.Select(address => address.ToString()).Order());
        }

        private static IPAddress[] GetAddresses()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(network => network.OperationalStatus == OperationalStatus.Up)
                .SelectMany(network => network.GetIPProperties().UnicastAddresses)
                .Select(unicast => unicast.Address)
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork && IsPermitted(address))
                .Distinct()
                .ToArray();
        }

        private static bool IsPermitted(IPAddress address)
        {
            var bytes = address.GetAddressBytes();

            return PermittedNetworks.Any(network =>
            {
                var mask = CreateMask(network.PrefixLength);
                var networkBytes = network.Address.GetAddressBytes();

                for (var index = 0; index < bytes.Length; index++)
                {
                    if ((bytes[index] & mask[index]) != networkBytes[index])
                    {
                        return false;
                    }
                }

                return true;
            });
        }

        private static X509Extension CreateNameConstraints()
        {
            var writer = new AsnWriter(AsnEncodingRules.DER);

            using (writer.PushSequence())
            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
            {
                foreach (var dnsName in DnsNames)
                {
                    using (writer.PushSequence())
                    {
                        writer.WriteCharacterString(UniversalTagNumber.IA5String, dnsName, new Asn1Tag(TagClass.ContextSpecific, 2));
                    }
                }

                foreach (var (address, prefixLength) in PermittedNetworks)
                {
                    using (writer.PushSequence())
                    {
                        writer.WriteOctetString([.. address.GetAddressBytes(), .. CreateMask(prefixLength)], new Asn1Tag(TagClass.ContextSpecific, 7));
                    }
                }
            }

            return new X509Extension(NameConstraintsOid, writer.Encode(), true);
        }

        private static byte[] CreateMask(int prefixLength)
        {
            var mask = new byte[4];

            for (var bit = 0; bit < prefixLength; bit++)
            {
                mask[bit / 8] |= (byte)(0x80 >> (bit % 8));
            }

            return mask;
        }

        private static byte[] CreateSerial()
        {
            var serial = RandomNumberGenerator.GetBytes(SerialLength);
            serial[0] = (byte)((serial[0] & 0x3F) | 0x40);
            return serial;
        }
    }
}
