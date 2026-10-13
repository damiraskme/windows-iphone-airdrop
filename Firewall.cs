using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ClipboardPhonePc
{
    public static class Firewall
    {
        private const string ShellCharacters = "&|^%<>\"";
        private const string MarkerSuffix = " rule 3";

        public static async Task<bool> EnsureAsync(int port)
        {
            return await IsCurrentAsync(port) || await AllowAsync(port);
        }

        public static async Task<bool> AllowAsync(int port)
        {
            var name = App.AppName;
            var scope = ProgramPath is { } path ? $"program=\"{path}\" protocol=TCP" : $"protocol=TCP localport={Ports(port)}";
            var command = $"/c netsh advfirewall firewall delete rule name=\"{name}\""
                + $" & netsh advfirewall firewall add rule name=\"{name}\" description=\"{Marker}\" dir=in action=allow {scope}"
                + " remoteip=localsubnet profile=private";

            var allowed = await RunElevatedAsync(command);
            AppLog.Info(allowed ? "Firewall rule set for private networks" : "Firewall rule was not set");
            return allowed;
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
                    && output.Contains(Marker, StringComparison.Ordinal)
                    && (ProgramPath is { } path
                        ? output.Contains(path, StringComparison.OrdinalIgnoreCase)
                        : Regex.IsMatch(output, $@":\s+{Ports(port)}\s*$", RegexOptions.Multiline));
            }
            catch (Win32Exception)
            {
                return false;
            }
        }

        private static string Marker => App.AppName + MarkerSuffix;

        private static string? ProgramPath => Environment.ProcessPath is { } path && path.IndexOfAny(ShellCharacters.ToCharArray()) < 0 ? path : null;

        private static string Ports(int port)
        {
            return $"{port},{CertificateServer.PreferredPort(port)}";
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
