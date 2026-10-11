using System.IO;
using System.Reflection;
using System.Windows;

namespace BlueDrop
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

            AppPaths.ClearTempDirectory();

            var settings = AppSettings.Load();
            var store = new TransferStore();
            var devices = new DeviceStore();
            var pairing = new PairingService(devices);
            var hotkeys = new HotkeyService(store);
            var certificates = new CertificateAuthority();
            var server = new TransferServer(store, devices, pairing, settings, certificates);

            hotkeys.Apply(settings.HotkeyModifiers, settings.HotkeyKey);

            _hotkeys = hotkeys;
            _server = server;
            _notifier = new ReceivedNotifier(store, settings);
            var settingsLauncher = new SettingsLauncher(() => new SettingsWindow(settings, devices, pairing, hotkeys, server, certificates));
            _trayIcon = new TrayIcon(new MainWindow(store, settingsLauncher), store, settingsLauncher);

            try
            {
                await server.StartAsync(settings.Port, settings.AllowNetwork);
            }
            catch (IOException)
            {
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_server is not null)
            {
                Task.Run(_server.StopAsync).Wait(StopTimeout);
            }

            AppPaths.ClearTempDirectory();

            _hotkeys?.Dispose();
            _trayIcon?.Dispose();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }

}
