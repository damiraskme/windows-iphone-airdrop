using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace BlueDrop
{
    public partial class MainWindow : Window
    {
        private const int ExtendedStyleIndex = -20;
        private const int ToolWindowStyle = 0x80;
        private const double CursorOffset = 8;
        private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(300);

        private DateTime _hiddenAt;

        public MainWindow()
        {
            InitializeComponent();
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

            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(handle, ExtendedStyleIndex, GetWindowLongPtr(handle, ExtendedStyleIndex) | ToolWindowStyle);
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

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern nint GetWindowLongPtr(nint handle, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern nint SetWindowLongPtr(nint handle, int index, nint value);
    }
}
