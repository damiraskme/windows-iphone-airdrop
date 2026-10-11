using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;

namespace BlueDrop
{
    public sealed record PairRequest(string? Name);

    public sealed class TransferServer
    {
        private const string TextContentType = "text/plain";
        private const string FallbackFileName = "file";
        private const string FallbackImageName = "image";
        private const string BearerPrefix = "Bearer ";
        private const string CertificatePath = "/certificate.cer";
        private const string CertificateContentType = "application/x-x509-ca-cert";
        private const string ProfilePath = "/certificate.mobileconfig";
        private const string ProfileContentType = "application/x-apple-aspen-config";
        private const long MaxPairBodyBytes = 4096;
        private const int MaxDeviceNameLength = 64;
        private const long MaxClipboardImageBytes = 64 * 1024 * 1024;

        private static readonly Dictionary<string, string> ClipboardImageExtensions = new()
        {
            ["image/png"] = ".png",
            ["image/jpeg"] = ".jpg",
            ["image/gif"] = ".gif",
            ["image/bmp"] = ".bmp",
            ["image/tiff"] = ".tif"
        };

        private static readonly object OfferedKey = new();
        private static readonly object SecuredKey = new();

        private readonly TransferStore _store;
        private readonly DeviceStore _devices;
        private readonly PairingService _pairing;
        private readonly AppSettings _settings;
        private readonly CertificateAuthority _certificates;
        private WebApplication? _app;
        private int _port;
        private bool _allowNetwork;

        public TransferServer(TransferStore store, DeviceStore devices, PairingService pairing, AppSettings settings, CertificateAuthority certificates)
        {
            _store = store;
            _devices = devices;
            _pairing = pairing;
            _settings = settings;
            _certificates = certificates;
        }

        public event Action? CertificateRejected;

        public static int CertificatePort(int port) => port < AppSettings.MaxPort ? port + 1 : port - 1;

        public static string ProfileUrl(int port) => $"http://{CertificateAuthority.LocalHostName}:{CertificatePort(port)}{ProfilePath}";

        public async Task StartAsync(int port, bool allowNetwork)
        {
            var app = Build(port, allowNetwork);

            try
            {
                await app.StartAsync();
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }

            _app = app;
            _port = port;
            _allowNetwork = allowNetwork;
        }

        public async Task StopAsync()
        {
            if (_app is null)
            {
                return;
            }

            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }

        public async Task<bool> TryRestartAsync(int port, bool allowNetwork)
        {
            var previousPort = _port;
            var previousAllowNetwork = _allowNetwork;
            var wasRunning = _app is not null;

            await StopAsync();

            try
            {
                await StartAsync(port, allowNetwork);
                return true;
            }
            catch (IOException)
            {
                if (wasRunning)
                {
                    await StartAsync(previousPort, previousAllowNetwork);
                }

                return false;
            }
        }

        private WebApplication Build(int port, bool allowNetwork)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrelHttpsConfiguration();
            builder.WebHost.ConfigureKestrel(options =>
            {
                if (allowNetwork)
                {
                    options.ListenAnyIP(port, UseCertificate);
                    options.ListenAnyIP(CertificatePort(port));
                }
                else
                {
                    options.ListenLocalhost(port, UseCertificate);
                }

                options.Limits.MaxRequestBodySize = null;
            });

            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                if (context.Request.IsHttps || context.Request.Path == CertificatePath || context.Request.Path == ProfilePath)
                {
                    await next(context);
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                }
            });

            app.MapGet(CertificatePath, () => Results.Bytes(_certificates.RootBytes, CertificateContentType));
            app.MapGet(ProfilePath, () => Results.Bytes(_certificates.CreateProfile(), ProfileContentType));
            app.MapGet("/ping", () => Results.Ok());
            app.MapPost("/pair", PairAsync);

            var transfers = app.MapGroup(string.Empty).AddEndpointFilter(AuthorizeAsync);
            transfers.MapPost("/send", ReceiveAsync);
            transfers.MapGet("/get", Send);

            return app;
        }

        private void UseCertificate(ListenOptions listener)
        {
            listener.Use(next => async connection =>
            {
                await next(connection);

                if (connection.Items.ContainsKey(OfferedKey)
                    && !connection.Items.ContainsKey(SecuredKey)
                    && connection.RemoteEndPoint is IPEndPoint remote
                    && !IPAddress.IsLoopback(remote.Address))
                {
                    CertificateRejected?.Invoke();
                }
            });

            listener.UseHttps(https => https.ServerCertificateSelector = (connection, name) =>
            {
                if (connection is not null && string.Equals(name, CertificateAuthority.LocalHostName, StringComparison.OrdinalIgnoreCase))
                {
                    connection.Items[OfferedKey] = true;
                }

                return _certificates.ServerCertificate;
            });

            listener.Use(next => connection =>
            {
                connection.Items[SecuredKey] = true;
                return next(connection);
            });
        }

        private async ValueTask<object?> AuthorizeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            if (!IsAuthorized(context.HttpContext.Request))
            {
                return Results.Unauthorized();
            }

            return await next(context);
        }

        private bool IsAuthorized(HttpRequest request)
        {
            var header = request.Headers.Authorization.ToString();
            return header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
                && _devices.Authorize(header[BearerPrefix.Length..].Trim());
        }

        private async Task<IResult> PairAsync(HttpRequest request)
        {
            var url = $"{request.Scheme}://{request.Host}";
            var name = Environment.MachineName;

            if (IsAuthorized(request))
            {
                return Results.Json(new { url, name, paired = true });
            }

            var key = _pairing.CompletePairing(request.Query["code"].ToString(), await ReadDeviceNameAsync(request));
            return key is null
                ? Results.Unauthorized()
                : Results.Json(new { key, url, name });
        }

        private static async Task<string> ReadDeviceNameAsync(HttpRequest request)
        {
            var name = request.Query["name"].ToString();

            if (name.Length == 0 && request.HasJsonContentType())
            {
                if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                {
                    limit.MaxRequestBodySize = MaxPairBodyBytes;
                }

                try
                {
                    name = (await request.ReadFromJsonAsync<PairRequest>())?.Name ?? string.Empty;
                }
                catch (JsonException)
                {
                }
            }

            name = name.Trim();
            return name.Length > MaxDeviceNameLength ? name[..MaxDeviceNameLength] : name;
        }

        private async Task<IResult> ReceiveAsync(HttpRequest request)
        {
            var contentType = (request.ContentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
            var name = SanitizeName(request.Query["name"].ToString());

            if (contentType == TextContentType)
            {
                using var reader = new StreamReader(request.Body, Encoding.UTF8);
                _store.Receive(TransferItem.FromText(await reader.ReadToEndAsync()));
            }
            else if (ClipboardImageExtensions.TryGetValue(contentType, out var extension))
            {
                using var buffer = new MemoryStream();
                await request.Body.CopyToAsync(buffer);

                var data = buffer.ToArray();
                var imageName = name.Length == 0 ? FallbackImageName + extension
                    : Path.HasExtension(name) ? name
                    : name + extension;

                string? path = null;
                if (_settings.SaveFiles)
                {
                    path = UniquePath(PrepareSaveDirectory(), imageName);
                    await File.WriteAllBytesAsync(path, data);
                }

                _store.Receive(TransferItem.FromImage(imageName, contentType, data, path));
            }
            else
            {
                var prefix = new byte[RtfText.SignatureLength];
                var prefixLength = await request.Body.ReadAtLeastAsync(prefix, prefix.Length, false);

                if (RtfText.HasSignature(prefix.AsSpan(0, prefixLength)))
                {
                    using var buffer = new MemoryStream();
                    buffer.Write(prefix, 0, prefixLength);
                    await request.Body.CopyToAsync(buffer);
                    _store.Receive(TransferItem.FromText(await RtfText.ToPlainTextAsync(buffer.ToArray())));
                }
                else
                {
                    var directory = _settings.SaveFiles ? PrepareSaveDirectory() : PrepareTempDirectory();
                    var path = UniquePath(directory, name.Length == 0 ? FallbackFileName : name);

                    await using (var file = File.Create(path))
                    {
                        await file.WriteAsync(prefix.AsMemory(0, prefixLength));
                        await request.Body.CopyToAsync(file);
                    }

                    _store.Receive(ToReceivedItem(path, name));
                }
            }

            return Results.Ok();
        }

        private IResult Send()
        {
            var item = _store.Outgoing;
            if (item is null || (item.Kind == TransferKind.File && !File.Exists(item.FilePath)))
            {
                return Results.NoContent();
            }

            if (_settings.ClearAfterFetch)
            {
                _store.ClearOutgoing(item);
            }

            return item.Kind switch
            {
                TransferKind.Text => Results.Text(item.Text, TextContentType, Encoding.UTF8),
                TransferKind.Image => Results.File(item.Data!, item.ContentType, item.Name),
                _ => Results.File(item.FilePath!, item.ContentType, item.Name)
            };
        }

        private TransferItem ToReceivedItem(string path, string name)
        {
            if (new FileInfo(path).Length > MaxClipboardImageBytes || ImageProbe.GetExtension(path) is not { } extension)
            {
                return TransferItem.FromFile(path);
            }

            var imageName = name.Length == 0 ? FallbackImageName + extension
                : Path.HasExtension(name) ? name
                : name + extension;
            var data = File.ReadAllBytes(path);

            string? imagePath = null;
            if (!_settings.SaveFiles)
            {
                File.Delete(path);
            }
            else if (Path.GetFileName(path) != imageName)
            {
                imagePath = UniquePath(Path.GetDirectoryName(path)!, imageName);
                File.Move(path, imagePath);
            }
            else
            {
                imagePath = path;
            }

            return TransferItem.FromImage(imageName, TransferItem.ContentTypeOf(imageName), data, imagePath);
        }

        private static string PrepareTempDirectory()
        {
            AppPaths.ClearTempDirectory();
            Directory.CreateDirectory(AppPaths.TempDirectory);
            return AppPaths.TempDirectory;
        }

        private string PrepareSaveDirectory()
        {
            var directory = string.IsNullOrWhiteSpace(_settings.SaveFolder) ? AppPaths.DefaultSaveDirectory : _settings.SaveFolder;

            try
            {
                Directory.CreateDirectory(directory);
                return directory;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Directory.CreateDirectory(AppPaths.DefaultSaveDirectory);
                return AppPaths.DefaultSaveDirectory;
            }
        }

        private static string SanitizeName(string name)
        {
            var fileName = Path.GetFileName(name);
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalid, '_');
            }

            return fileName.Trim();
        }

        private static string UniquePath(string directory, string name)
        {
            var path = Path.Combine(directory, name);
            var stem = Path.GetFileNameWithoutExtension(name);
            var extension = Path.GetExtension(name);

            for (var index = 1; File.Exists(path); index++)
            {
                path = Path.Combine(directory, $"{stem} ({index}){extension}");
            }

            return path;
        }
    }
}
