using System.ComponentModel;
using System.Diagnostics;

namespace ClipboardPhonePc
{
    public static class Firewall
    {
        private const string ShellCharacters = "&|^%<>\"";

        public static async Task<bool> AllowAsync(int port)
        {
            var name = App.AppName;
            var program = ProgramPath is { } path ? $" program=\"{path}\"" : string.Empty;
            var command = $"/c netsh advfirewall firewall delete rule name=\"{name}\""
                + $" & netsh advfirewall firewall add rule name=\"{name}\" dir=in action=allow{program} protocol=TCP localport={Ports(port)} remoteip=localsubnet";

            var allowed = await RunElevatedAsync(command);
            AppLog.Info(allowed ? $"Firewall rule set for ports {Ports(port)}" : "Firewall rule was not set");
            return allowed;
        }

        public static async Task<bool> RemoveAsync()
        {
            var removed = await RunElevatedAsync($"/c netsh advfirewall firewall delete rule name=\"{App.AppName}\"");
            AppLog.Info(removed ? "Firewall rule removed" : "Firewall rule was not removed");
            return removed;
        }

        public static async Task<bool> IsCurrentAsync(int port)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo("netsh.exe", $"advfirewall firewall show rule name=\"{App.AppName}\" verbose")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                });

                if (process is null)
                {
                    return false;
                }

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                return process.ExitCode == 0
                    && output.Contains(Ports(port), StringComparison.Ordinal)
                    && (ProgramPath is not { } path || output.Contains(path, StringComparison.OrdinalIgnoreCase));
            }
            catch (Win32Exception)
            {
                return false;
            }
        }

        private static string? ProgramPath => Environment.ProcessPath is { } path && path.IndexOfAny(ShellCharacters.ToCharArray()) < 0 ? path : null;

        private static string Ports(int port)
        {
            return $"{port},{TransferServer.CertificatePort(port)}";
        }

        private static async Task<bool> RunElevatedAsync(string command)
        {
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
