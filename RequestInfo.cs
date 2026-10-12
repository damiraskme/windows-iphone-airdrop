using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace ClipboardPhonePc
{
    public static class RequestInfo
    {
        private const string UnknownSource = "unknown";

        public static string Source(HttpContext context)
        {
            return context.Connection.RemoteIpAddress is { } address ? Format(address) : UnknownSource;
        }

        public static string Format(IPAddress address)
        {
            return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
        }

        public static void LimitBody(HttpRequest request, long bytes)
        {
            if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            {
                limit.MaxRequestBodySize = bytes;
            }
        }
    }
}
