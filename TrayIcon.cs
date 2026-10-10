using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace BlueDrop
{
    public sealed class TrayIcon : IDisposable
    {
        private readonly MainWindow _window;
        private readonly Icon _icon;
        private readonly Forms.NotifyIcon _notifyIcon;

        public TrayIcon(MainWindow window)
        {
            _window = window;
            _icon = CreateIcon();

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Exit", null, (_, _) => System.Windows.Application.Current.Shutdown());

            _notifyIcon = new Forms.NotifyIcon
            {
                Icon = _icon,
                Text = App.AppName,
                ContextMenuStrip = menu,
                Visible = true
            };
            _notifyIcon.MouseClick += OnMouseClick;
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _icon.Dispose();
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

        private static Icon CreateIcon()
        {
            using var bitmap = new Bitmap(32, 32);
            using (var graphics = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(Color.FromArgb(0, 120, 212)))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                graphics.FillEllipse(brush, 4, 4, 24, 24);
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
