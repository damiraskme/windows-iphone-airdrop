using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ClipboardPhonePc
{
    public static class WindowInterop
    {
        public const int ToolWindowStyle = 0x80;
        public const int NoActivateStyle = 0x08000000;

        private const int ExtendedStyleIndex = -20;
        private const int DarkTitleBarAttribute = 20;

        public static void AddExtendedStyle(Window window, int style)
        {
            var handle = new WindowInteropHelper(window).Handle;
            SetWindowLongPtr(handle, ExtendedStyleIndex, GetWindowLongPtr(handle, ExtendedStyleIndex) | style);
        }

        public static void UseDarkTitleBar(Window window)
        {
            var enabled = 1;
            DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, DarkTitleBarAttribute, ref enabled, sizeof(int));
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern nint GetWindowLongPtr(nint handle, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern nint SetWindowLongPtr(nint handle, int index, nint value);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(nint handle, int attribute, ref int value, int size);
    }
}
