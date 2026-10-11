using System.ComponentModel;
using System.Diagnostics;

namespace BlueDrop
{
    public static class Firewall
    {
        public static async Task<bool> AllowAsync(int port)
        {
            var name = App.AppName;
            var command = $"/c netsh advfirewall firewall delete rule name=\"{name}\""
                + $" & netsh advfirewall firewall add rule name=\"{name}\" dir=in action=allow protocol=TCP localport={port},{TransferServer.CertificatePort(port)} remoteip=localsubnet";

            try
            {
                using var process = Process.Start(new ProcessStartInfo("cmd.exe", command)
                {
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                if (process is null)
                {
                    return false;
                }

                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch (Win32Exception)
            {
                return false;
            }
        }
    }
}
