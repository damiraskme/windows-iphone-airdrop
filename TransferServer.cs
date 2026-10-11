using System.IO;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace BlueDrop
{
    public sealed class TransferServer
    {
        private const string TextContentType = "text/plain";
        private const string FallbackFileName = "file";
        private const string BearerPrefix = "Bearer ";

        private static readonly HashSet<string> ClipboardImageTypes =
        [
            "image/png",
            "image/jpeg",
            "image/gif",
            "image/bmp",
            "image/tiff"
        ];

        private readonly TransferStore _store;
        private readonly DeviceStore _devices;
        private readonly PairingService _pairing;
        private readonly AppSettings _settings;
        private WebApplication? _app;
        private int _port;

        public TransferServer(TransferStore store, DeviceStore devices, PairingService pairing, AppSettings settings)
        {
            _store = store;
            _devices = devices;
            _pairing = pairing;
            _settings = settings;
        }

        public async Task StartAsync(int port)
        {
            var app = Build(port);

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

        public async Task<bool> TryRestartAsync(int port)
        {
            var previousPort = _port;
            var wasRunning = _app is not null;

            await StopAsync();

            try
            {
                await StartAsync(port);
                return true;
            }
            catch (IOException)
            {
                if (wasRunning)
                {
                    await StartAsync(previousPort);
                }

                return false;
            }
        }

        private WebApplication Build(int port)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenLocalhost(port);
                options.Limits.MaxRequestBodySize = null;
            });

            var app = builder.Build();
            app.MapGet("/ping", () => Results.Ok());
            app.MapPost("/pair", Pair);

            var transfers = app.MapGroup(string.Empty).AddEndpointFilter(AuthorizeAsync);
            transfers.MapPost("/send", ReceiveAsync);
            transfers.MapGet("/get", Send);

            return app;
        }

        private async ValueTask<object?> AuthorizeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var header = context.HttpContext.Request.Headers.Authorization.ToString();
            if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
                || !_devices.Authorize(header[BearerPrefix.Length..].Trim()))
            {
                return Results.Unauthorized();
            }

            return await next(context);
        }

        private IResult Pair(HttpRequest request)
        {
            var key = _pairing.CompletePairing(request.Query["code"].ToString(), request.Query["name"].ToString());
            return key is null ? Results.Unauthorized() : Results.Json(new { key });
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
            else if (ClipboardImageTypes.Contains(contentType))
            {
                using var buffer = new MemoryStream();
                await request.Body.CopyToAsync(buffer);
                _store.Receive(TransferItem.FromImage(name, contentType, buffer.ToArray()));
            }
            else
            {
                Directory.CreateDirectory(AppPaths.InboxDirectory);
                var path = UniquePath(AppPaths.InboxDirectory, name);

                await using (var file = File.Create(path))
                {
                    await request.Body.CopyToAsync(file);
                }

                _store.Receive(TransferItem.FromFile(path));
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

        private static string SanitizeName(string name)
        {
            var fileName = Path.GetFileName(name);
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalid, '_');
            }

            return string.IsNullOrWhiteSpace(fileName) ? FallbackFileName : fileName;
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
