using System.IO;
using System.Net;
using System.Net.Security;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Logging;

namespace ClipboardPhonePc
{
    public sealed class TransferServer
    {
        private const string TextContentType = "text/plain";
        private const string CertificatePath = "/certificate.cer";
        private const string CertificateContentType = "application/x-x509-ca-cert";
        private const string ProfilePath = "/certificate.mobileconfig";
        private const string ProfileContentType = "application/x-apple-aspen-config";
        private const long BytesPerMegabyte = 1024 * 1024;
        private const long MaxTextBytes = 8 * BytesPerMegabyte;

        private static readonly TimeSpan RejectionGrace = TimeSpan.FromSeconds(2);
        private static readonly object OfferedKey = new();
        private static readonly object SecuredKey = new();

        private readonly TransferStore _store;
        private readonly AppSettings _settings;
        private readonly CertificateAuthority _certificates;
        private readonly DeviceAccess _access;
        private readonly FileReceiver _receiver;
        private WebApplication? _app;
        private int _port;
        private bool _allowNetwork;
        private long _securedAt = long.MinValue / 2;

        public TransferServer(TransferStore store, DeviceStore devices, PairingService pairing, AppSettings settings, CertificateAuthority certificates)
        {
            _store = store;
            _settings = settings;
            _certificates = certificates;
            _access = new DeviceAccess(devices, pairing);
            _receiver = new FileReceiver(settings);
        }

        public event Action? CertificateRejected;

        public static int CertificatePort(int port) => port < AppSettings.MaxPort ? port + 1 : port - 1;

        public string CertificateLink(int port)
        {
            return $"http://{CertificateAuthority.LocalHostName}:{CertificatePort(port)}{ProfilePath}#{Convert.ToBase64String(_certificates.RootBytes)}";
        }

        public async Task StartAsync(int port, bool allowNetwork)
        {
            var app = Build(port, allowNetwork);

            try
            {
                await app.StartAsync();
            }
            catch (Exception e)
            {
                AppLog.Error($"Server could not start on port {port}", e);
                AppStatus.Report(StatusIssue.Server, string.Format(AppStatus.ServerNotStarted, port));
                await app.DisposeAsync();
                throw;
            }

            _app = app;
            _port = port;
            _allowNetwork = allowNetwork;

            AppStatus.Resolve(StatusIssue.Server);
            AppLog.Info(allowNetwork
                ? $"Server started on port {port}, certificate download on port {CertificatePort(port)}"
                : $"Server started on port {port}, this PC only");
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

            AppLog.Info("Server stopped");
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
            });

            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                if (!context.Request.IsHttps && context.Request.Path != CertificatePath && context.Request.Path != ProfilePath)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                try
                {
                    await next(context);
                }
                catch (Microsoft.AspNetCore.Http.BadHttpRequestException e)
                {
                    AppLog.Warn($"{context.Request.Method} {context.Request.Path} from {DeviceAccess.Describe(context)} rejected, {e.Message}");
                    throw;
                }
                catch (Exception e) when (context.RequestAborted.IsCancellationRequested)
                {
                    AppLog.Warn($"{context.Request.Method} {context.Request.Path} from {DeviceAccess.Describe(context)} was cancelled by the device, {e.Message}");
                    throw;
                }
                catch (Exception e)
                {
                    AppLog.Error($"{context.Request.Method} {context.Request.Path} from {DeviceAccess.Describe(context)} failed", e);
                    throw;
                }
            });

            app.MapGet(CertificatePath, DownloadCertificate);
            app.MapGet(ProfilePath, DownloadProfile);
            app.MapGet("/ping", () => Results.Ok());
            app.MapPost("/pair", _access.PairAsync);

            var transfers = app.MapGroup(string.Empty).AddEndpointFilter(_access.AuthorizeAsync);
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
                    var rejectedAt = Environment.TickCount64;
                    await Task.Delay(RejectionGrace);

                    if (Volatile.Read(ref _securedAt) < rejectedAt - (long)RejectionGrace.TotalMilliseconds)
                    {
                        AppLog.Warn($"Certificate not trusted by {RequestInfo.Format(remote.Address)}");
                        CertificateRejected?.Invoke();
                    }
                }
            });

            listener.UseHttps(new TlsHandshakeCallbackOptions
            {
                OnConnection = context =>
                {
                    if (string.Equals(context.ClientHelloInfo.ServerName, CertificateAuthority.LocalHostName, StringComparison.OrdinalIgnoreCase))
                    {
                        context.Connection.Items[OfferedKey] = true;
                    }

                    return ValueTask.FromResult(new SslServerAuthenticationOptions
                    {
                        ServerCertificateContext = _certificates.ServerContext
                    });
                }
            });

            listener.Use(next => connection =>
            {
                connection.Items[SecuredKey] = true;
                Volatile.Write(ref _securedAt, Environment.TickCount64);
                return next(connection);
            });
        }

        private IResult DownloadCertificate(HttpRequest request)
        {
            AppLog.Info($"Certificate downloaded by {RequestInfo.Source(request.HttpContext)}");
            return Results.Bytes(_certificates.RootBytes, CertificateContentType);
        }

        private IResult DownloadProfile(HttpRequest request)
        {
            AppLog.Info($"Certificate profile downloaded by {RequestInfo.Source(request.HttpContext)}");
            return Results.Bytes(_certificates.CreateProfile(), ProfileContentType);
        }

        private async Task<IResult> ReceiveAsync(HttpRequest request)
        {
            var contentType = (request.ContentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
            TransferItem item;

            if (contentType == TextContentType)
            {
                RequestInfo.LimitBody(request, MaxTextBytes);

                using var reader = new StreamReader(request.Body, Encoding.UTF8);
                item = TransferItem.FromText(await reader.ReadToEndAsync());
            }
            else
            {
                RequestInfo.LimitBody(request, _settings.MaxFileMegabytes * BytesPerMegabyte);
                item = await _receiver.ReceiveAsync(request.Body, request.Query["name"].ToString(), contentType);
            }

            _store.Receive(item);
            AppLog.Info($"Received {Describe(item)} from {DeviceAccess.Describe(request.HttpContext)}");
            return Results.Ok();
        }

        private IResult Send(HttpRequest request)
        {
            var context = request.HttpContext;
            var item = _store.Outgoing;

            if (item is null || (item.Kind == TransferKind.File && !File.Exists(item.FilePath)))
            {
                AppLog.Info($"Nothing to send to {DeviceAccess.Describe(context)}");
                return Results.NoContent();
            }

            var description = $"{Describe(item)} to {DeviceAccess.Describe(context)}";

            context.Response.OnCompleted(() =>
            {
                if (context.RequestAborted.IsCancellationRequested)
                {
                    AppLog.Warn($"Sending {description} was interrupted");
                }
                else
                {
                    AppLog.Info($"Sent {description}");

                    if (_settings.ClearAfterFetch)
                    {
                        _store.ClearOutgoing(item);
                    }
                }

                return Task.CompletedTask;
            });

            return item.Kind switch
            {
                TransferKind.Text => Results.Text(item.Text, TextContentType, Encoding.UTF8),
                TransferKind.Image => Results.File(item.Data!, item.ContentType, item.Name),
                _ => Results.File(item.FilePath!, item.ContentType, item.Name)
            };
        }

        private static string Describe(TransferItem item)
        {
            return item.Kind switch
            {
                TransferKind.Text => $"text ({item.Text!.Length} characters)",
                TransferKind.Image => $"image \"{item.Name}\" ({item.Data!.Length} bytes)",
                _ => $"file \"{item.Name}\" ({new FileInfo(item.FilePath!).Length} bytes)"
            };
        }
    }
}
