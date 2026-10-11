using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace BlueDrop
{
    public sealed class TrayIcon : IDisposable
    {
        private readonly MainWindow _window;
        private readonly TransferStore _store;
        private readonly Func<SettingsWindow> _createSettingsWindow;
        private readonly Icon _icon;
        private readonly Icon _pendingIcon;
        private readonly Forms.NotifyIcon _notifyIcon;
        private SettingsWindow? _settingsWindow;

        public TrayIcon(MainWindow window, TransferStore store, Func<SettingsWindow> createSettingsWindow)
        {
            _window = window;
            _store = store;
            _createSettingsWindow = createSettingsWindow;
            _icon = CreateIcon(false);
            _pendingIcon = CreateIcon(true);

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Settings", null, (_, _) => OpenSettings());
            menu.Items.Add("Exit", null, (_, _) => System.Windows.Application.Current.Shutdown());

            _notifyIcon = new Forms.NotifyIcon
            {
                Icon = _icon,
                Text = App.AppName,
                ContextMenuStrip = menu,
                Visible = true
            };
            _notifyIcon.MouseClick += OnMouseClick;

            _store.Changed += () => System.Windows.Application.Current.Dispatcher.BeginInvoke(UpdateIcon);
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _icon.Dispose();
            _pendingIcon.Dispose();
        }

        private void OnMouseClick(object? sender, Forms.MouseEventArgs e)
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                var cursor = Forms.Cursor.Position;
                var workArea = Forms.Screen.FromPoint(cursor).WorkingArea;
                _window.Toggle(new System.Windows.Point(cursor.X, cursor.Y), new System.Windows.Rect(workArea.X, workArea.Y, workArea.Width, workArea.Height));
            }
        }

        private void OpenSettings()
        {
            if (_settingsWindow is null)
            {
                _settingsWindow = _createSettingsWindow();
                _settingsWindow.Closed += (_, _) => _settingsWindow = null;
                _settingsWindow.Show();
            }

            _settingsWindow.Activate();
        }

        private void UpdateIcon()
        {
            _notifyIcon.Icon = _store.IncomingPending ? _pendingIcon : _icon;
        }

        private static Icon CreateIcon(bool pending)
        {
            using var bitmap = new Bitmap(32, 32);
            using (var graphics = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(Color.FromArgb(0, 120, 212)))
            using (var pendingBrush = new SolidBrush(Color.FromArgb(255, 82, 82)))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                graphics.FillEllipse(brush, 4, 4, 24, 24);

                if (pending)
                {
                    graphics.FillEllipse(pendingBrush, 17, 1, 14, 14);
                }
            }

            var handle = bitmap.GetHicon();
            try
            {
                return (Icon)Icon.FromHandle(handle).Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(nint handle);
    }
}
