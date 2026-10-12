using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace ClipboardPhonePc
{
    public sealed class TrayIcon : IDisposable
    {
        private readonly MainWindow _window;
        private readonly TransferStore _store;
        private const int MaxTextLength = 127;
        private const string Ellipsis = "...";

        private readonly Icon[] _icons;
        private readonly Forms.NotifyIcon _notifyIcon;

        public TrayIcon(MainWindow window, TransferStore store, SettingsLauncher settingsLauncher)
        {
            _window = window;
            _store = store;
            _icons = [CreateIcon(false, false), CreateIcon(true, false), CreateIcon(false, true), CreateIcon(true, true)];

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Settings", null, (_, _) => settingsLauncher.Open());
            menu.Items.Add("Log", null, (_, _) => Process.Start(new ProcessStartInfo(AppPaths.LogFile) { UseShellExecute = true }));
            menu.Items.Add("Exit", null, (_, _) => System.Windows.Application.Current.Shutdown());

            _notifyIcon = new Forms.NotifyIcon
            {
                Icon = _icons[0],
                Text = App.AppName,
                ContextMenuStrip = menu,
                Visible = true
            };
            _notifyIcon.MouseClick += OnMouseClick;

            _store.Changed += () => System.Windows.Application.Current.Dispatcher.BeginInvoke(UpdateIcon);
            AppStatus.Changed += () => System.Windows.Application.Current.Dispatcher.BeginInvoke(UpdateIcon);
            UpdateIcon();
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();

            foreach (var icon in _icons)
            {
                icon.Dispose();
            }
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

        private void UpdateIcon()
        {
            var messages = AppStatus.Messages;
            var text = string.Join(Environment.NewLine, messages.Prepend(App.AppName));

            _notifyIcon.Icon = _icons[(_store.IncomingPending ? 1 : 0) + (messages.Count > 0 ? 2 : 0)];
            _notifyIcon.Text = text.Length > MaxTextLength ? text[..(MaxTextLength - Ellipsis.Length)] + Ellipsis : text;
        }

        private static Icon CreateIcon(bool pending, bool problem)
        {
            using var bitmap = new Bitmap(32, 32);
            using (var graphics = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(Color.FromArgb(0, 120, 212)))
            using (var pendingBrush = new SolidBrush(Color.FromArgb(255, 82, 82)))
            using (var problemBrush = new SolidBrush(Color.FromArgb(255, 185, 0)))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                graphics.FillEllipse(brush, 4, 4, 24, 24);

                if (pending)
                {
                    graphics.FillEllipse(pendingBrush, 17, 1, 14, 14);
                }

                if (problem)
                {
                    graphics.FillEllipse(problemBrush, 17, 17, 14, 14);
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
