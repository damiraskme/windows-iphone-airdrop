using System.Formats.Asn1;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace BlueDrop
{
    public sealed class CertificateAuthority
    {
        private const string NameConstraintsOid = "2.5.29.30";
        private const string ServerAuthOid = "1.3.6.1.5.5.7.3.1";
        private const string KeyNameSuffix = " Root";
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
        private readonly ECDsa _rootKey;
        private readonly X509Certificate2 _root;
        private X509Certificate2? _server;
        private volatile bool _serverStale;

        public CertificateAuthority()
        {
            (_rootKey, _root) = LoadRoot() ?? CreateRoot();
            NetworkChange.NetworkAddressChanged += (_, _) => _serverStale = true;
        }

        public static string HostName { get; } = Dns.GetHostName().ToLowerInvariant();

        public static string LocalHostName => HostName + LocalSuffix;

        public byte[] RootBytes => _root.RawData;

        public string Fingerprint => string.Join(' ', _root.GetCertHashString(HashAlgorithmName.SHA256)
            .Chunk(FingerprintGroupLength)
            .Select(group => new string(group)));

        public X509Certificate2 ServerCertificate
        {
            get
            {
                lock (_sync)
                {
                    if (_server is null || _serverStale || DateTime.Now > _server.NotAfter - RenewBefore)
                    {
                        _serverStale = false;
                        _server = IssueServer();
                    }

                    return _server;
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

        private static string KeyName => App.AppName + KeyNameSuffix;

        private static string[] DnsNames => [LocalHostName, HostName, Localhost];

        private static (ECDsa Key, X509Certificate2 Certificate)? LoadRoot()
        {
            if (!File.Exists(AppPaths.CertificateFile) || !CngKey.Exists(KeyName))
            {
                return null;
            }

            try
            {
                var certificate = X509CertificateLoader.LoadCertificateFromFile(AppPaths.CertificateFile);
                var key = new ECDsaCng(CngKey.Open(KeyName));
                using var publicKey = certificate.GetECDsaPublicKey();

                if (publicKey is not null
                    && certificate.GetNameInfo(X509NameType.SimpleName, false) == RootName
                    && certificate.NotAfter > DateTime.Now.AddDays(ServerDays)
                    && publicKey.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(key.ExportSubjectPublicKeyInfo()))
                {
                    return (key, certificate);
                }

                key.Dispose();
                certificate.Dispose();
                return null;
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        private static (ECDsa Key, X509Certificate2 Certificate) CreateRoot()
        {
            if (CngKey.Exists(KeyName))
            {
                using var previous = CngKey.Open(KeyName);
                previous.Delete();
            }

            var key = new ECDsaCng(CngKey.Create(CngAlgorithm.ECDsaP256, KeyName, new CngKeyCreationParameters
            {
                ExportPolicy = CngExportPolicies.None
            }));

            var subject = new X500DistinguishedNameBuilder();
            subject.AddCommonName(RootName);
            var name = subject.Build();

            var request = new CertificateRequest(name, key, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            request.CertificateExtensions.Add(CreateNameConstraints());

            var now = DateTimeOffset.Now;
            var certificate = request.Create(name, X509SignatureGenerator.CreateForECDsa(key), now - ClockSkew, now.AddYears(RootYears), CreateSerial());

            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllBytes(AppPaths.CertificateFile, certificate.RawData);
            return (key, certificate);
        }

        private X509Certificate2 IssueServer()
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            var subject = new X500DistinguishedNameBuilder();
            subject.AddCommonName(LocalHostName);

            var names = new SubjectAlternativeNameBuilder();
            foreach (var dnsName in DnsNames)
            {
                names.AddDnsName(dnsName);
            }

            foreach (var address in GetAddresses())
            {
                names.AddIpAddress(address);
            }

            var request = new CertificateRequest(subject.Build(), key, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(ServerAuthOid)], false));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(_root, true, false));
            request.CertificateExtensions.Add(names.Build());

            var now = DateTimeOffset.Now;
            using var signed = request.Create(
                _root.SubjectName,
                X509SignatureGenerator.CreateForECDsa(_rootKey),
                now - ClockSkew,
                now.AddDays(ServerDays),
                CreateSerial());
            using var exportable = signed.CopyWithPrivateKey(key);

            return X509CertificateLoader.LoadPkcs12(exportable.Export(X509ContentType.Pfx), null);
        }

        private static IEnumerable<IPAddress> GetAddresses()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(network => network.OperationalStatus == OperationalStatus.Up)
                .SelectMany(network => network.GetIPProperties().UnicastAddresses)
                .Select(unicast => unicast.Address)
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork && IsPermitted(address))
                .Distinct();
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
