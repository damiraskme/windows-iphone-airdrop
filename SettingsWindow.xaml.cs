using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ClipboardPhonePc
{
    public sealed record DeviceRow(string Id, string Name, string Details);

    public partial class SettingsWindow : Window
    {
        private const string PairText = "Pair";
        private const string PairedText = "Paired";
        private const string LastUsedText = "Last used";
        private const string NotConnectedText = "Not connected yet";
        private const string RemoveQuestion = "Remove \"{0}\"?";
        private const string DateFormat = "d";
        private const string DateTimeFormat = "g";
        private const int CodeGroupLength = 3;

        private readonly AppSettings _settings;
        private readonly DeviceStore _devices;
        private readonly PairingService _pairing;
        private readonly HotkeyService _hotkeys;
        private readonly TransferServer _server;
        private readonly CertificateAuthority _certificates;
        private readonly CertificateServer _certificateServer;
        private readonly DispatcherTimer _pairingTimer;
        private readonly Action _onDevicesChanged;
        private readonly Action _onPairingChanged;
        private readonly Action _onCertificateRejected;

        public SettingsWindow(AppSettings settings, DeviceStore devices, PairingService pairing, HotkeyService hotkeys, TransferServer server, CertificateAuthority certificates, CertificateServer certificateServer)
        {
            InitializeComponent();

            _settings = settings;
            _devices = devices;
            _pairing = pairing;
            _hotkeys = hotkeys;
            _server = server;
            _certificates = certificates;
            _certificateServer = certificateServer;

            _onDevicesChanged = () => Dispatcher.BeginInvoke(RefreshDevices);
            _onPairingChanged = () => Dispatcher.BeginInvoke(RefreshPairing);
            _onCertificateRejected = () => Dispatcher.BeginInvoke(ShowCertificateForPairingAsync);
            _devices.Changed += _onDevicesChanged;
            _pairing.Changed += _onPairingChanged;
            _server.CertificateRejected += _onCertificateRejected;

            _pairingTimer = new DispatcherTimer { Interval = PairingService.CodeLifetime };
            _pairingTimer.Tick += (_, _) =>
            {
                _pairingTimer.Stop();
                RefreshPairing();
            };

            PopupCheckBox.IsChecked = settings.PopupEnabled;
            HideContentCheckBox.IsChecked = settings.HidePopupContent;
            AutoCopyCheckBox.IsChecked = settings.AutoCopy;
            SoundCheckBox.IsChecked = settings.SoundEnabled;
            SaveFilesCheckBox.IsChecked = settings.SaveFiles;
            SaveFolderRow.IsEnabled = settings.SaveFiles;
            SaveFolderBox.Text = CurrentSaveFolder;
            ClearAfterFetchCheckBox.IsChecked = settings.ClearAfterFetch;
            AutoStartCheckBox.IsChecked = AutoStart.IsEnabled;
            DurationBox.Text = settings.PopupSeconds.ToString();
            PortBox.Text = settings.Port.ToString();
            MaxFileBox.Text = settings.MaxFileMegabytes.ToString();
            HotkeyBox.Text = HotkeyService.Format(settings.HotkeyModifiers, settings.HotkeyKey);

            RefreshDevices();
            RefreshPairing();
            AppStatus.Acknowledge();
        }

        private string CurrentSaveFolder => string.IsNullOrWhiteSpace(_settings.SaveFolder) ? AppPaths.DefaultSaveDirectory : _settings.SaveFolder;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowInterop.UseDarkTitleBar(this);
        }

        protected override void OnClosed(EventArgs e)
        {
            _pairingTimer.Stop();
            _devices.Changed -= _onDevicesChanged;
            _pairing.Changed -= _onPairingChanged;
            _server.CertificateRejected -= _onCertificateRejected;
            _ = _certificateServer.StopAsync();
            base.OnClosed(e);
        }

        private void OnOptionClick(object sender, RoutedEventArgs e)
        {
            _settings.PopupEnabled = PopupCheckBox.IsChecked == true;
            _settings.HidePopupContent = HideContentCheckBox.IsChecked == true;
            _settings.AutoCopy = AutoCopyCheckBox.IsChecked == true;
            _settings.SoundEnabled = SoundCheckBox.IsChecked == true;
            _settings.SaveFiles = SaveFilesCheckBox.IsChecked == true;
            _settings.ClearAfterFetch = ClearAfterFetchCheckBox.IsChecked == true;
            _settings.Save();

            SaveFolderRow.IsEnabled = _settings.SaveFiles;
        }

        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { InitialDirectory = CurrentSaveFolder };
            if (dialog.ShowDialog(this) == true)
            {
                _settings.SaveFolder = dialog.FolderName;
                _settings.Save();
                SaveFolderBox.Text = CurrentSaveFolder;
            }
        }

        private void OnAutoStartClick(object sender, RoutedEventArgs e)
        {
            AutoStart.Set(AutoStartCheckBox.IsChecked == true);
        }

        private void OnFieldKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }

        private void OnDurationCommit(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (int.TryParse(DurationBox.Text, out var seconds)
                && seconds is >= AppSettings.MinPopupSeconds and <= AppSettings.MaxPopupSeconds)
            {
                _settings.PopupSeconds = seconds;
                _settings.Save();
            }

            DurationBox.Text = _settings.PopupSeconds.ToString();
        }

        private async void OnPortCommit(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (int.TryParse(PortBox.Text, out var port)
                && port is >= AppSettings.MinPort and <= AppSettings.MaxPort
                && port != _settings.Port)
            {
                PortBox.IsEnabled = false;

                if (await Firewall.EnsureAsync(port) && await _server.TryRestartAsync(port))
                {
                    AppLog.Info($"Port changed from {_settings.Port} to {port}");
                    AppStatus.Resolve(StatusIssue.Firewall);
                    _settings.Port = port;
                    _settings.Save();
                }
                else
                {
                    AppLog.Warn($"Port stays {_settings.Port}, could not switch to {port}");
                }

                PortBox.IsEnabled = true;
            }

            PortBox.Text = _settings.Port.ToString();
            RefreshPairing();
        }

        private void OnMaxFileCommit(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (int.TryParse(MaxFileBox.Text, out var megabytes)
                && megabytes is >= AppSettings.MinFileMegabytes and <= AppSettings.FileMegabytesLimit
                && megabytes != _settings.MaxFileMegabytes)
            {
                AppLog.Info($"Max file size changed from {_settings.MaxFileMegabytes} MB to {megabytes} MB");
                _settings.MaxFileMegabytes = megabytes;
                _settings.Save();
            }

            MaxFileBox.Text = _settings.MaxFileMegabytes.ToString();
        }

        private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var modifiers = Keyboard.Modifiers;

            if (modifiers == ModifierKeys.None)
            {
                if (key is Key.Back or Key.Delete)
                {
                    SetHotkey(0, 0);
                    e.Handled = true;
                }
                else if (key == Key.Escape)
                {
                    Keyboard.ClearFocus();
                    e.Handled = true;
                }

                return;
            }

            e.Handled = true;

            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            {
                return;
            }

            SetHotkey((int)modifiers, KeyInterop.VirtualKeyFromKey(key));
        }

        private void SetHotkey(int modifiers, int key)
        {
            if (!_hotkeys.Apply(modifiers, key))
            {
                modifiers = 0;
                key = 0;
            }

            _settings.HotkeyModifiers = modifiers;
            _settings.HotkeyKey = key;
            _settings.Save();
            AppStatus.Resolve(StatusIssue.Hotkey);

            HotkeyBox.Text = HotkeyService.Format(modifiers, key);
        }

        private async void OnCertificateClick(object sender, RoutedEventArgs e)
        {
            if (CertificatePanel.Visibility == Visibility.Visible)
            {
                await HideCertificateAsync();
                return;
            }

            await EnsureFirewallAsync(sender);
            await ShowCertificateAsync();
        }

        private async Task ShowCertificateAsync()
        {
            if (!await _certificateServer.StartAsync(CertificateServer.PreferredPort(_settings.Port)))
            {
                return;
            }

            CertificateQrImage.Source = QrCode.Create(_certificateServer.Url);
            FingerprintText.Text = _certificates.Fingerprint;
            CertificatePanel.Visibility = Visibility.Visible;
        }

        private async Task HideCertificateAsync()
        {
            CertificatePanel.Visibility = Visibility.Collapsed;
            await _certificateServer.StopAsync();
        }

        private async Task EnsureFirewallAsync(object sender)
        {
            if (sender is not UIElement button)
            {
                return;
            }

            button.IsEnabled = false;

            if (await Firewall.EnsureAsync(_settings.Port))
            {
                AppStatus.Resolve(StatusIssue.Firewall);
            }
            else
            {
                AppStatus.Report(StatusIssue.Firewall, AppStatus.FirewallOutdated);
            }

            button.IsEnabled = true;
        }

        private async void OnPairClick(object sender, RoutedEventArgs e)
        {
            await HideCertificateAsync();
            await EnsureFirewallAsync(sender);
            _pairingTimer.Stop();
            _pairing.BeginPairing();
            _pairingTimer.Start();
        }

        private void OnNameCommit(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox { Tag: DeviceRow row } box && box.Text != row.Name)
            {
                _devices.Rename(row.Id, box.Text.Trim());
            }
        }

        private void OnRemoveClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: DeviceRow row }
                && MessageBox.Show(this, string.Format(RemoveQuestion, row.Name), Title, MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                _devices.Remove(row.Id);
            }
        }

        private void RefreshPairing()
        {
            var code = _pairing.ActiveCode;
            PairLabel.Text = code is null ? PairText : code.Insert(CodeGroupLength, " ");

            if (code is null)
            {
                QrBorder.Visibility = Visibility.Collapsed;
                return;
            }

            var host = _settings.UseAddress && _certificates.AllowsAddresses && CertificateAuthority.PrimaryAddress is { } address
                ? address.ToString()
                : CertificateAuthority.LocalHostName;

            QrImage.Source = QrCode.Create($"https://{host}:{_settings.Port}/pair?code={code}");
            QrBorder.Visibility = Visibility.Visible;
        }

        private async Task ShowCertificateForPairingAsync()
        {
            if (_pairing.ActiveCode is null)
            {
                return;
            }

            QrBorder.Visibility = Visibility.Collapsed;
            await ShowCertificateAsync();
        }

        private void RefreshDevices()
        {
            DeviceList.ItemsSource = _devices.GetAll()
                .Select(device => new DeviceRow(
                    device.Id,
                    string.IsNullOrWhiteSpace(device.Name) ? device.Id[..DeviceStore.ShortIdLength] : device.Name,
                    FormatDetails(device)))
                .ToList();
        }

        private static string FormatDetails(PairedDevice device)
        {
            var paired = $"{PairedText} {device.PairedAt.ToLocalTime().ToString(DateFormat)}";
            return device.LastUsedAt is { } lastUsed
                ? $"{paired}\n{LastUsedText} {lastUsed.ToLocalTime().ToString(DateTimeFormat)}"
                : $"{paired}\n{NotConnectedText}";
        }
    }
}
