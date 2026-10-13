using System.Windows;
using System.Windows.Controls;

namespace ClipboardPhonePc
{
    public partial class DebugWindow : Window
    {
        private const string PendingTag = "Pending";
        private const string ProblemTag = "Problem";
        private const string BothTag = "Both";
        private const string NothingText = "Nothing waiting";
        private const int SamplePort = 5055;
        private const string SampleHotkey = "Ctrl+Alt+V";

        private static readonly Dictionary<StatusIssue, string> SampleMessages = new()
        {
            [StatusIssue.Server] = string.Format(AppStatus.ServerNotStarted, SamplePort),
            [StatusIssue.Download] = AppStatus.DownloadNotStarted,
            [StatusIssue.Hotkey] = string.Format(AppStatus.HotkeyTaken, SampleHotkey),
            [StatusIssue.Firewall] = AppStatus.FirewallOutdated,
            [StatusIssue.Network] = AppStatus.NetworkPublic,
            [StatusIssue.Certificate] = AppStatus.CertificateReplaced,
            [StatusIssue.Settings] = AppStatus.SettingsReset,
            [StatusIssue.Devices] = AppStatus.DevicesReset
        };

        private readonly TrayIcon _trayIcon;
        private readonly ReceivedNotifier _notifier;
        private readonly DeviceStore _devices;
        private readonly TransferStore _store;

        public DebugWindow(TrayIcon trayIcon, ReceivedNotifier notifier, DeviceStore devices, TransferStore store)
        {
            InitializeComponent();

            _trayIcon = trayIcon;
            _notifier = notifier;
            _devices = devices;
            _store = store;

            foreach (var issue in SampleMessages.Keys)
            {
                var button = new Button
                {
                    Style = (Style)FindResource("TileButton"),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 0, 4, 4),
                    Content = new TextBlock { Text = issue.ToString() },
                    Tag = issue
                };
                button.Click += OnMessageToggleClick;
                MessagePanel.Children.Add(button);
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowInterop.UseDarkTitleBar(this);
        }

        protected override void OnClosed(EventArgs e)
        {
            _trayIcon.ResetIcon();
            base.OnClosed(e);
        }

        private void OnIconClick(object sender, RoutedEventArgs e)
        {
            var tag = (sender as FrameworkElement)?.Tag as string;
            _trayIcon.PreviewIcon(tag is PendingTag or BothTag, tag is ProblemTag or BothTag);
        }

        private void OnIconResetClick(object sender, RoutedEventArgs e)
        {
            _trayIcon.ResetIcon();
        }

        private void OnMessageToggleClick(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: StatusIssue issue })
            {
                return;
            }

            if (AppStatus.Has(issue))
            {
                AppStatus.Resolve(issue);
            }
            else
            {
                AppStatus.Report(issue, SampleMessages[issue]);
            }
        }

        private void OnMessageAddClick(object sender, RoutedEventArgs e)
        {
            AppStatus.Report(StatusIssue.Debug, CustomMessageBox.Text);
        }

        private void OnMessageClearClick(object sender, RoutedEventArgs e)
        {
            AppStatus.Resolve(Enum.GetValues<StatusIssue>());
        }

        private void OnPopupClick(object sender, RoutedEventArgs e)
        {
            _notifier.ShowMessage(PopupBox.Text);
        }

        private void OnPairClick(object sender, RoutedEventArgs e)
        {
            KeyBox.Text = _devices.Add(DeviceNameBox.Text.Trim(), out var device);
            AppLog.Info($"Debug paired \"{device.Name}\" ({device.Id[..DeviceStore.ShortIdLength]})");
        }

        private void OnSendClick(object sender, RoutedEventArgs e)
        {
            _store.Receive(TransferItem.FromText(TransferBox.Text));
        }

        private void OnGetClick(object sender, RoutedEventArgs e)
        {
            if (_store.Outgoing is not { } item)
            {
                TransferBox.Text = NothingText;
                return;
            }

            ClipboardService.Write(item);
            TransferBox.Text = item.Preview;
        }
    }
}
