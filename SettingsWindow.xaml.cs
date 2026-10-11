using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace BlueDrop
{
    public sealed record DeviceRow(string Id, string Name, string Details);

    public partial class SettingsWindow : Window
    {
        private const string PairText = "Pair";
        private const string PairedText = "Paired";
        private const string LastUsedText = "Last used";
        private const string RemoveQuestion = "Remove \"{0}\"?";
        private const string DateFormat = "d";
        private const string DateTimeFormat = "g";
        private const int CodeGroupLength = 3;
        private const int ShortIdLength = 8;

        private readonly AppSettings _settings;
        private readonly DeviceStore _devices;
        private readonly PairingService _pairing;
        private readonly HotkeyService _hotkeys;
        private readonly TransferServer _server;
        private readonly DispatcherTimer _pairingTimer;
        private readonly Action _onDevicesChanged;
        private readonly Action _onPairingChanged;

        public SettingsWindow(AppSettings settings, DeviceStore devices, PairingService pairing, HotkeyService hotkeys, TransferServer server)
        {
            InitializeComponent();

            _settings = settings;
            _devices = devices;
            _pairing = pairing;
            _hotkeys = hotkeys;
            _server = server;

            _onDevicesChanged = () => Dispatcher.BeginInvoke(RefreshDevices);
            _onPairingChanged = () => Dispatcher.BeginInvoke(RefreshPairing);
            _devices.Changed += _onDevicesChanged;
            _pairing.Changed += _onPairingChanged;

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
            ClearAfterFetchCheckBox.IsChecked = settings.ClearAfterFetch;
            AutoStartCheckBox.IsChecked = AutoStart.IsEnabled;
            DurationBox.Text = settings.PopupSeconds.ToString();
            PortBox.Text = settings.Port.ToString();
            HotkeyBox.Text = HotkeyService.Format(settings.HotkeyModifiers, settings.HotkeyKey);

            RefreshDevices();
            RefreshPairing();
        }

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
            base.OnClosed(e);
        }

        private void OnOptionClick(object sender, RoutedEventArgs e)
        {
            _settings.PopupEnabled = PopupCheckBox.IsChecked == true;
            _settings.HidePopupContent = HideContentCheckBox.IsChecked == true;
            _settings.AutoCopy = AutoCopyCheckBox.IsChecked == true;
            _settings.SoundEnabled = SoundCheckBox.IsChecked == true;
            _settings.ClearAfterFetch = ClearAfterFetchCheckBox.IsChecked == true;
            _settings.Save();
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

                if (await _server.TryRestartAsync(port))
                {
                    _settings.Port = port;
                    _settings.Save();
                }

                PortBox.IsEnabled = true;
            }

            PortBox.Text = _settings.Port.ToString();
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

            HotkeyBox.Text = HotkeyService.Format(modifiers, key);
        }

        private void OnPairClick(object sender, RoutedEventArgs e)
        {
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
        }

        private void RefreshDevices()
        {
            DeviceList.ItemsSource = _devices.GetAll()
                .Select(device => new DeviceRow(
                    device.Id,
                    string.IsNullOrWhiteSpace(device.Name) ? device.Id[..ShortIdLength] : device.Name,
                    FormatDetails(device)))
                .ToList();
        }

        private static string FormatDetails(PairedDevice device)
        {
            var paired = $"{PairedText} {device.PairedAt.ToLocalTime().ToString(DateFormat)}";
            return device.LastUsedAt is { } lastUsed
                ? $"{paired}\n{LastUsedText} {lastUsed.ToLocalTime().ToString(DateTimeFormat)}"
                : paired;
        }
    }
}
