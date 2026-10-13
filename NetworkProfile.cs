using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

namespace ClipboardPhonePc
{
    public static class NetworkProfile
    {
        private const int PublicCategory = 0;
        private const int ConnectedNetworks = 1;
        private static readonly Guid NetworkListManager = new("DCB00C01-570F-4A9B-8D69-199FDBA5723B");

        public static void Check()
        {
            if (IsPublic())
            {
                AppStatus.Report(StatusIssue.Network, AppStatus.NetworkPublic);
            }
            else
            {
                AppStatus.Resolve(StatusIssue.Network);
            }
        }

        private static bool IsPublic()
        {
            try
            {
                if (Type.GetTypeFromCLSID(NetworkListManager) is not { } type || Activator.CreateInstance(type) is not { } instance)
                {
                    return false;
                }

                dynamic manager = instance;
                var categories = new List<(bool Online, int Category)>();

                foreach (dynamic network in manager.GetNetworks(ConnectedNetworks))
                {
                    categories.Add(((bool)network.IsConnectedToInternet, (int)network.GetCategory()));
                }

                var relevant = categories.Any(network => network.Online) ? categories.Where(network => network.Online) : categories;
                return relevant.Any(network => network.Category == PublicCategory);
            }
            catch (Exception e) when (e is COMException or RuntimeBinderException or InvalidCastException or ArgumentException)
            {
                AppLog.Warn($"Could not read the network type, {e.Message}");
                return false;
            }
        }
    }
}
