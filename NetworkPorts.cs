using System.Net;
using System.Net.Sockets;

namespace ClipboardPhonePc
{
    public static class NetworkPorts
    {
        public static bool IsFree(int port)
        {
            return CanBind(IPAddress.Any, port) && CanBind(IPAddress.IPv6Any, port);
        }

        public static int FindFree()
        {
            using var listener = new TcpListener(IPAddress.Any, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        private static bool CanBind(IPAddress address, int port)
        {
            try
            {
                using var listener = new TcpListener(address, port) { ExclusiveAddressUse = true };
                listener.Start();
                return true;
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.AddressFamilyNotSupported)
            {
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }
    }
}
