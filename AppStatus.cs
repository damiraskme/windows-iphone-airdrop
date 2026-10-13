namespace ClipboardPhonePc
{
    public enum StatusIssue
    {
        Server,
        Hotkey,
        Download,
        Firewall,
        Network,
        Certificate,
        Settings,
        Devices,
        Debug
    }

    public static class AppStatus
    {
        public const string ServerNotStarted = "Server could not start on port {0}";
        public const string DownloadNotStarted = "Certificate download could not start";
        public const string HotkeyTaken = "Hotkey {0} is taken";
        public const string FirewallOutdated = "Firewall rule is missing or outdated";
        public const string NetworkPublic = "Network is set to Public, the phone is blocked";
        public const string CertificateReplaced = "New certificate, install it on the phone again";
        public const string SettingsRestored = "Settings were restored from backup";
        public const string SettingsReset = "Settings were reset";
        public const string DevicesRestored = "Devices were restored from backup";
        public const string DevicesReset = "Devices were reset, pair the phone again";

        private static readonly StatusIssue[] Notices = [StatusIssue.Certificate, StatusIssue.Settings, StatusIssue.Devices];
        private static readonly object Sync = new();
        private static readonly SortedDictionary<StatusIssue, string> Issues = [];

        public static event Action? Changed;

        public static IReadOnlyList<string> Messages
        {
            get
            {
                lock (Sync)
                {
                    return [.. Issues.Values];
                }
            }
        }

        public static bool Has(StatusIssue issue)
        {
            lock (Sync)
            {
                return Issues.ContainsKey(issue);
            }
        }

        public static void Report(StatusIssue issue, string message)
        {
            lock (Sync)
            {
                if (Issues.TryGetValue(issue, out var current) && current == message)
                {
                    return;
                }

                Issues[issue] = message;
            }

            AppLog.Warn(message);
            Changed?.Invoke();
        }

        public static void Resolve(params StatusIssue[] issues)
        {
            var removed = false;

            lock (Sync)
            {
                foreach (var issue in issues)
                {
                    removed |= Issues.Remove(issue);
                }
            }

            if (removed)
            {
                Changed?.Invoke();
            }
        }

        public static void Acknowledge()
        {
            Resolve(Notices);
        }
    }
}
