using System.Collections.Concurrent;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ClipboardPhonePc
{
    public sealed class CertificateServer
    {
        private const string StartPath = "/";
        private const string ProbePath = "/probe";
        private const string DecidePath = "/decide";
        private const string ProfilePath = "/certificate.mobileconfig";
        private const string ProfileContentType = "application/x-apple-aspen-config";
        private const string PageContentType = "text/html";
        private const string TokenParameter = "t";
        private const int FreePortAttempts = 3;

        private const string Page = """
            <!doctype html>
            <meta name="viewport" content="width=device-width">
            <title>{0}</title>
            <script>
            new Image().src = "http://{1}:{2}{3}?{4}={5}";
            var next = function () {{ location.href = "{6}"; }};
            fetch("{7}?{4}={5}").then(next, next);
            </script>
            """;

        private static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(4);

        private readonly CertificateAuthority _certificates;
        private readonly TimeSpan _probeTimeout;
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _probes = new();
        private WebApplication? _app;

        public CertificateServer(CertificateAuthority certificates, TimeSpan? probeTimeout = null)
        {
            _certificates = certificates;
            _probeTimeout = probeTimeout ?? DefaultProbeTimeout;
        }

        public event Action<bool>? LocalNameChecked;

        public int Port { get; private set; }

        public string Url => CertificateAuthority.PrimaryAddress is { } address
            ? $"http://{address}:{Port}{StartPath}"
            : $"http://{CertificateAuthority.LocalHostName}:{Port}{ProfilePath}";

        public static int PreferredPort(int port) => port < AppSettings.MaxPort ? port + 1 : port - 1;

        public async Task<bool> StartAsync(int preferredPort)
        {
            if (_app is not null)
            {
                return true;
            }

            for (var attempt = 0; attempt <= FreePortAttempts; attempt++)
            {
                var port = attempt == 0 ? preferredPort : NetworkPorts.FindFree();

                if (!NetworkPorts.IsFree(port))
                {
                    AppLog.Warn($"Certificate download could not use port {port}");
                    continue;
                }

                var app = Build(port);

                try
                {
                    await app.StartAsync();
                }
                catch (IOException)
                {
                    AppLog.Warn($"Certificate download could not use port {port}");
                    await app.DisposeAsync();
                    continue;
                }

                _app = app;
                Port = port;
                AppStatus.Resolve(StatusIssue.Download);
                AppLog.Info($"Certificate download started on port {port}");
                return true;
            }

            AppStatus.Report(StatusIssue.Download, AppStatus.DownloadNotStarted);
            return false;
        }

        public async Task StopAsync()
        {
            if (_app is null)
            {
                return;
            }

            var app = _app;
            _app = null;

            await app.StopAsync();
            await app.DisposeAsync();
            _probes.Clear();

            AppLog.Info("Certificate download stopped");
        }

        private WebApplication Build(int port)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(port));

            var app = builder.Build();
            app.MapGet(StartPath, Start);
            app.MapGet(ProbePath, Probe);
            app.MapGet(DecidePath, DecideAsync);
            app.MapGet(ProfilePath, Download);
            return app;
        }

        private IResult Start()
        {
            var token = Guid.NewGuid().ToString("N");
            _probes[token] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            return Results.Content(
                string.Format(Page, App.AppName, CertificateAuthority.LocalHostName, Port, ProbePath, TokenParameter, token, ProfilePath, DecidePath),
                PageContentType);
        }

        private IResult Probe(HttpRequest request)
        {
            if (_probes.TryGetValue(request.Query[TokenParameter].ToString(), out var probe))
            {
                probe.TrySetResult();
            }

            return Results.NoContent();
        }

        private async Task<IResult> DecideAsync(HttpRequest request)
        {
            if (!_probes.TryRemove(request.Query[TokenParameter].ToString(), out var probe))
            {
                return Results.NotFound();
            }

            var source = RequestInfo.Source(request.HttpContext);
            var works = await Task.WhenAny(probe.Task, Task.Delay(_probeTimeout)) == probe.Task;

            if (works)
            {
                AppLog.Info($"{source} can reach this PC by name");
            }
            else
            {
                AppLog.Warn($"{source} cannot reach this PC by name, using its IP address instead");
                _certificates.EnableAddresses();
            }

            LocalNameChecked?.Invoke(works);
            return Results.Ok();
        }

        private IResult Download(HttpRequest request)
        {
            AppLog.Info($"Certificate downloaded by {RequestInfo.Source(request.HttpContext)}");
            return Results.Bytes(_certificates.CreateProfile(), ProfileContentType);
        }
    }
}
