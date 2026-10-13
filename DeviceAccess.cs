using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace ClipboardPhonePc
{
    public sealed class DeviceAccess
    {
        private const string BearerPrefix = "Bearer ";
        private const long MaxPairBodyBytes = 4096;
        private const int MaxDeviceNameLength = 64;

        private static readonly object DeviceKey = new();

        private readonly DeviceStore _devices;
        private readonly PairingService _pairing;

        public DeviceAccess(DeviceStore devices, PairingService pairing)
        {
            _devices = devices;
            _pairing = pairing;
        }

        public static PairedDevice? Device(HttpContext context)
        {
            return context.Items[DeviceKey] as PairedDevice;
        }

        public static string Describe(HttpContext context)
        {
            return context.Items[DeviceKey] is PairedDevice device ? Describe(device) : RequestInfo.Source(context);
        }

        public async ValueTask<object?> AuthorizeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var http = context.HttpContext;

            if (Authenticate(http.Request) is not { } device)
            {
                var reason = http.Request.Headers.Authorization.ToString().Trim().Length <= BearerPrefix.Trim().Length ? "no key sent" : "unknown key";
                AppLog.Warn($"Rejected {http.Request.Method} {http.Request.Path} from {RequestInfo.Source(http)}, {reason}");
                return Results.Unauthorized();
            }

            http.Items[DeviceKey] = device;
            return await next(context);
        }

        public async Task<IResult> PairAsync(HttpRequest request)
        {
            var source = RequestInfo.Source(request.HttpContext);
            var url = $"{request.Scheme}://{request.Host}";
            var name = Environment.MachineName;

            if (Authenticate(request) is { } device)
            {
                AppLog.Info($"Pairing skipped, {Describe(device)} from {source} is already paired");
                return Results.Json(new { url, name, paired = true });
            }

            var key = await _pairing.CompletePairingAsync(request.Query["code"].ToString(), await ReadDeviceNameAsync(request), source);
            return key is null
                ? Results.Unauthorized()
                : Results.Json(new { key, url, name });
        }

        private static string Describe(PairedDevice device)
        {
            return $"\"{device.Name}\" ({device.Id[..DeviceStore.ShortIdLength]})";
        }

        private PairedDevice? Authenticate(HttpRequest request)
        {
            var header = request.Headers.Authorization.ToString();
            return header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
                ? _devices.Authorize(header[BearerPrefix.Length..].Trim())
                : null;
        }

        private static async Task<string> ReadDeviceNameAsync(HttpRequest request)
        {
            var name = request.Query["name"].ToString();

            if (name.Length == 0 && request.HasJsonContentType())
            {
                RequestInfo.LimitBody(request, MaxPairBodyBytes);

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
    }
}
