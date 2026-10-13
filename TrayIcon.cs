using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace ClipboardPhonePc
{
    public sealed class TrayIcon : IDisposable
    {
        private const int IconSize = 32;
        private const int MaxTextLength = 127;
        private const string Ellipsis = "...";
        private const string ThemeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string LightThemeValue = "SystemUsesLightTheme";

        private readonly MainWindow _window;
        private readonly TransferStore _store;
        private readonly Icon[] _icons;
        private readonly Forms.NotifyIcon _notifyIcon;
        private readonly Forms.ContextMenuStrip _menu;
        private readonly Forms.ToolStripItem _firewallItem;

        public TrayIcon(MainWindow window, TransferStore store, SettingsLauncher settingsLauncher, AppSettings settings)
        {
            _window = window;
            _store = store;
            _icons = [CreateIcon(false, false), CreateIcon(true, false), CreateIcon(false, true), CreateIcon(true, true)];

            _menu = new Forms.ContextMenuStrip();
            _firewallItem = _menu.Items.Add("Fix firewall", null, async (_, _) => await FixFirewallAsync(settings.Port));
            _menu.Items.Add("Settings", null, (_, _) => settingsLauncher.Open());
            _menu.Items.Add("Log", null, (_, _) => Process.Start(new ProcessStartInfo(AppPaths.LogFile) { UseShellExecute = true }));
            _menu.Items.Add("Exit", null, (_, _) => System.Windows.Application.Current.Shutdown());

            _notifyIcon = new Forms.NotifyIcon
            {
                Icon = _icons[0],
                ContextMenuStrip = _menu,
                Visible = true
            };
            _notifyIcon.MouseClick += OnMouseClick;

            _store.Changed += () => System.Windows.Application.Current.Dispatcher.BeginInvoke(UpdateIcon);
            AppStatus.Changed += () => System.Windows.Application.Current.Dispatcher.BeginInvoke(UpdateIcon);
            UpdateIcon();
        }

        public void AddMenuItem(string text, Action action)
        {
            _menu.Items.Insert(_menu.Items.Count - 1, new Forms.ToolStripMenuItem(text, null, (_, _) => action()));
        }

        public void PreviewIcon(bool pending, bool problem)
        {
            _notifyIcon.Icon = _icons[IconIndex(pending, problem)];
        }

        public void ResetIcon()
        {
            UpdateIcon();
        }

        public void ShowWindow()
        {
            var workArea = Forms.Screen.PrimaryScreen!.WorkingArea;
            _window.ShowAt(
                new System.Windows.Point(workArea.Right, workArea.Bottom),
                new System.Windows.Rect(workArea.X, workArea.Y, workArea.Width, workArea.Height));
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

        private async Task FixFirewallAsync(int port)
        {
            _firewallItem.Enabled = false;

            if (await Firewall.AllowAsync(port))
            {
                AppStatus.Resolve(StatusIssue.Firewall);
            }

            _firewallItem.Enabled = true;
        }

        private void UpdateIcon()
        {
            _firewallItem.Visible = AppStatus.Has(StatusIssue.Firewall);

            var messages = AppStatus.Messages;
            var text = string.Join(Environment.NewLine, messages.Prepend($"{App.AppName} {App.Version}"));

            _notifyIcon.Icon = _icons[IconIndex(_store.IncomingPending, messages.Count > 0)];
            _notifyIcon.Text = text.Length > MaxTextLength ? text[..(MaxTextLength - Ellipsis.Length)] + Ellipsis : text;
        }

        private static int IconIndex(bool pending, bool problem)
        {
            return (pending ? 1 : 0) + (problem ? 2 : 0);
        }

        private static bool UsesLightTaskbar()
        {
            using var key = Registry.CurrentUser.OpenSubKey(ThemeKey);
            return key?.GetValue(LightThemeValue) is int value && value != 0;
        }

        private static Icon CreateIcon(bool pending, bool problem)
        {
            var color = UsesLightTaskbar() ? System.Windows.Media.Colors.Black : System.Windows.Media.Colors.White;

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(IconArt.Render(IconSize, color, pending, problem)));

            using var stream = new MemoryStream();
            encoder.Save(stream);
            stream.Position = 0;

            using var bitmap = new Bitmap(stream);
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
