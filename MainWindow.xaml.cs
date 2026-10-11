using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BlueDrop
{
    public partial class MainWindow : Window
    {
        private const double CursorOffset = 8;
        private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(300);

        private readonly TransferStore _store;
        private readonly SettingsLauncher _settingsLauncher;
        private DateTime _hiddenAt;

        public MainWindow(TransferStore store, SettingsLauncher settingsLauncher)
        {
            InitializeComponent();

            _settingsLauncher = settingsLauncher;
            _store = store;
            _store.Changed += () => Dispatcher.BeginInvoke(Refresh);

            Refresh();
        }

        public void Toggle(Point cursor, Rect workArea)
        {
            if (IsVisible)
            {
                Hide();
                return;
            }

            if (DateTime.UtcNow - _hiddenAt < ReopenGuard)
            {
                return;
            }

            var dpi = VisualTreeHelper.GetDpi(this);
            var cursorX = cursor.X / dpi.DpiScaleX;
            var cursorY = cursor.Y / dpi.DpiScaleY;

            var left = cursorX + CursorOffset;
            if (left + Width > workArea.Right / dpi.DpiScaleX)
            {
                left = cursorX - CursorOffset - Width;
            }

            Left = Math.Max(left, workArea.Left / dpi.DpiScaleX);
            Top = Math.Max(cursorY - CursorOffset - Height, workArea.Top / dpi.DpiScaleY);

            Show();
            Activate();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowInterop.AddExtendedStyle(this, WindowInterop.ToolWindowStyle);
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);

            if (!IsVisible)
            {
                return;
            }

            _hiddenAt = DateTime.UtcNow;
            Hide();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
            }
            else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Paste();
                e.Handled = true;
            }
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Get();
                e.Handled = true;
            }
        }

        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            Hide();
            _settingsLauncher.Open();
        }

        private void OnPasteClick(object sender, RoutedEventArgs e)
        {
            Paste();
        }

        private void OnGetClick(object sender, RoutedEventArgs e)
        {
            Get();
        }

        private void Paste()
        {
            var item = ClipboardService.Read();
            if (item is not null)
            {
                _store.Outgoing = item;
            }
        }

        private void Get()
        {
            var item = _store.Incoming;
            if (item is not null && ClipboardService.Write(item))
            {
                _store.MarkIncomingTaken();
                Hide();
            }
        }

        private void Refresh()
        {
            var incoming = _store.Incoming;

            OutgoingPreview.Text = _store.Outgoing?.Preview;
            IncomingPreview.Text = incoming?.Preview;
            GetButton.IsEnabled = incoming is not null;
        }
    }
}
