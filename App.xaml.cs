using System.IO;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Windows;

namespace ClipboardPhonePc
{
    public partial class App : Application
    {
        private const string ActivationSuffix = " Activate";
        private const string CrashText = "{0} has stopped because of an error.\n\nDetails are in the log:\n{1}";
        private const string PairQuestion = "Allow \"{0}\" ({1}) to pair?";
        private const string PairUnnamedQuestion = "Allow a new device ({0}) to pair?";
        private const int VersionParts = 3;

        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ConfirmTimeout = TimeSpan.FromSeconds(60);

        public static string AppName { get; } = Assembly.GetExecutingAssembly().GetName().Name!;

        public static string Version { get; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString(VersionParts) ?? string.Empty;

        private Mutex? _mutex;
        private EventWaitHandle? _activation;
        private TrayIcon? _trayIcon;
        private TransferServer? _server;
        private CertificateServer? _certificateServer;
        private ReceivedNotifier? _notifier;
        private HotkeyService? _hotkeys;
        private bool _isFirstInstance;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _mutex = new Mutex(true, AppName, out _isFirstInstance);
            _activation = new EventWaitHandle(false, EventResetMode.AutoReset, AppName + ActivationSuffix);

            if (!_isFirstInstance)
            {
                WindowInterop.AllowForeground();
                _activation.Set();
                Shutdown();
                return;
            }

            DispatcherUnhandledException += (_, args) =>
            {
                AppLog.Error("Unhandled error", args.Exception);
                ShowCrash();
                args.Handled = true;
                Shutdown(1);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                AppLog.Error("Unhandled error", args.ExceptionObject as Exception);
                ShowCrash();
            };
            TaskScheduler.UnobservedTaskException += (_, args) => AppLog.Error("Unhandled background error", args.Exception);

            AppLog.Info($"{AppName} {Version} started");
            AppPaths.ClearTempDirectory();
            AutoStart.Repair();

            var settings = AppSettings.Load();
            var store = new TransferStore();
            var devices = new DeviceStore();
            var pairing = new PairingService(devices) { Confirm = ConfirmPairingAsync };
            var hotkeys = new HotkeyService(store);
            var certificates = new CertificateAuthority();
            var server = new TransferServer(store, devices, pairing, settings, certificates);

            if (!hotkeys.Apply(settings.HotkeyModifiers, settings.HotkeyKey))
            {
                AppStatus.Report(StatusIssue.Hotkey, string.Format(AppStatus.HotkeyTaken, HotkeyService.Format(settings.HotkeyModifiers, settings.HotkeyKey)));
            }

            var certificateServer = new CertificateServer(certificates);
            certificateServer.LocalNameChecked += works =>
            {
                settings.UseAddress = !works;
                settings.Save();
            };

            _hotkeys = hotkeys;
            _server = server;
            _certificateServer = certificateServer;
            _notifier = new ReceivedNotifier(store, settings, pairing);
            var settingsLauncher = new SettingsLauncher(() => new SettingsWindow(settings, devices, pairing, hotkeys, server, certificates, certificateServer));
            _trayIcon = new TrayIcon(new MainWindow(store, settingsLauncher), store, settingsLauncher, settings);

            server.DeviceConnected += device => Dispatcher.BeginInvoke(() => _notifier.ShowConnected(device));

#if DEBUG
            DebugWindow? debug = null;
            _trayIcon.AddMenuItem("Debug", () =>
            {
                if (debug is null)
                {
                    debug = new DebugWindow(_trayIcon, _notifier, devices, store);
                    debug.Closed += (_, _) => debug = null;
                    debug.Show();
                }

                debug.Activate();
            });
#endif

            ThreadPool.RegisterWaitForSingleObject(
                _activation,
                (_, _) => Dispatcher.BeginInvoke(() => _trayIcon?.ShowWindow()),
                null,
                Timeout.Infinite,
                false);

            NetworkChange.NetworkAddressChanged += (_, _) => Task.Run(NetworkProfile.Check);

            if (certificates.Replaced)
            {
                _notifier.ShowMessage(AppStatus.CertificateReplaced);
            }

            try
            {
                await server.StartAsync(settings.Port);
            }
            catch (IOException)
            {
                return;
            }

            await Task.Run(NetworkProfile.Check);

            if (devices.GetAll().Count > 0 && !await Firewall.IsCurrentAsync(settings.Port))
            {
                AppStatus.Report(StatusIssue.Firewall, AppStatus.FirewallOutdated);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_isFirstInstance)
            {
                if (_certificateServer is not null)
                {
                    Task.Run(_certificateServer.StopAsync).Wait(StopTimeout);
                }

                if (_server is not null)
                {
                    Task.Run(_server.StopAsync).Wait(StopTimeout);
                }

                AppPaths.ClearTempDirectory();
                AppLog.Info($"{AppName} exited");
            }

            _hotkeys?.Dispose();
            _trayIcon?.Dispose();
            _activation?.Dispose();
            _mutex?.Dispose();
            base.OnExit(e);
        }

        private static void ShowCrash()
        {
            MessageBox.Show(string.Format(CrashText, AppName, AppPaths.LogFile), AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private async Task<bool> ConfirmPairingAsync(string name, string source)
        {
            var question = name.Length == 0 ? string.Format(PairUnnamedQuestion, source) : string.Format(PairQuestion, name, source);

            return await Dispatcher.InvokeAsync(() => new PairPrompt(question, ConfirmTimeout).ShowDialog() == true);
        }
    }
}
