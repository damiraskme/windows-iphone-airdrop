using System.IO;
using System.Reflection;
using System.Windows;

namespace ClipboardPhonePc
{
    public partial class App : Application
    {
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

        public static string AppName { get; } = Assembly.GetExecutingAssembly().GetName().Name!;

        private Mutex? _mutex;
        private TrayIcon? _trayIcon;
        private TransferServer? _server;
        private ReceivedNotifier? _notifier;
        private HotkeyService? _hotkeys;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _mutex = new Mutex(true, AppName, out var isFirstInstance);
            if (!isFirstInstance)
            {
                Shutdown();
                return;
            }

            DispatcherUnhandledException += (_, args) => AppLog.Error("Unhandled error", args.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, args) => AppLog.Error("Unhandled error", args.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, args) => AppLog.Error("Unhandled background error", args.Exception);

            AppLog.Info($"{AppName} {Assembly.GetExecutingAssembly().GetName().Version} started");
            AppPaths.ClearTempDirectory();

            var settings = AppSettings.Load();
            var store = new TransferStore();
            var devices = new DeviceStore();
            var pairing = new PairingService(devices);
            var hotkeys = new HotkeyService(store);
            var certificates = new CertificateAuthority();
            var server = new TransferServer(store, devices, pairing, settings, certificates);

            if (!hotkeys.Apply(settings.HotkeyModifiers, settings.HotkeyKey))
            {
                AppStatus.Report(StatusIssue.Hotkey, string.Format(AppStatus.HotkeyTaken, HotkeyService.Format(settings.HotkeyModifiers, settings.HotkeyKey)));
            }

            _hotkeys = hotkeys;
            _server = server;
            _notifier = new ReceivedNotifier(store, settings, pairing);
            var settingsLauncher = new SettingsLauncher(() => new SettingsWindow(settings, devices, pairing, hotkeys, server, certificates));
            _trayIcon = new TrayIcon(new MainWindow(store, settingsLauncher), store, settingsLauncher);

            if (certificates.Replaced)
            {
                _notifier.ShowMessage(AppStatus.CertificateReplaced);
            }

            try
            {
                await server.StartAsync(settings.Port, settings.AllowNetwork);
            }
            catch (IOException)
            {
                return;
            }

            if (settings.AllowNetwork && !await Firewall.IsCurrentAsync(settings.Port))
            {
                AppStatus.Report(StatusIssue.Firewall, AppStatus.FirewallOutdated);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_server is not null)
            {
                Task.Run(_server.StopAsync).Wait(StopTimeout);
            }

            AppPaths.ClearTempDirectory();
            AppLog.Info($"{AppName} exited");

            _hotkeys?.Dispose();
            _trayIcon?.Dispose();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }

}
