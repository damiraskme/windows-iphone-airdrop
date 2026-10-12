using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace ClipboardPhonePc
{
    public sealed class HotkeyService : IDisposable
    {
        private const int HotkeyId = 1;
        private const int HotkeyMessage = 0x0312;
        private const uint NoRepeat = 0x4000;
        private const int MessageOnlyParent = -3;

        private readonly TransferStore _store;
        private readonly HwndSource _source;
        private bool _registered;

        public HotkeyService(TransferStore store)
        {
            _store = store;
            _source = new HwndSource(new HwndSourceParameters(App.AppName)
            {
                ParentWindow = MessageOnlyParent,
                WindowStyle = 0
            });
            _source.AddHook(OnMessage);
        }

        public static string Format(int modifiers, int key)
        {
            if (key == 0)
            {
                return string.Empty;
            }

            var keys = (ModifierKeys)modifiers;
            var parts = new List<string>();

            if (keys.HasFlag(ModifierKeys.Control))
            {
                parts.Add("Ctrl");
            }

            if (keys.HasFlag(ModifierKeys.Alt))
            {
                parts.Add("Alt");
            }

            if (keys.HasFlag(ModifierKeys.Shift))
            {
                parts.Add("Shift");
            }

            if (keys.HasFlag(ModifierKeys.Windows))
            {
                parts.Add("Win");
            }

            parts.Add(KeyInterop.KeyFromVirtualKey(key).ToString());
            return string.Join("+", parts);
        }

        public bool Apply(int modifiers, int key)
        {
            if (_registered)
            {
                UnregisterHotKey(_source.Handle, HotkeyId);
                _registered = false;
            }

            if (key == 0)
            {
                return true;
            }

            _registered = RegisterHotKey(_source.Handle, HotkeyId, (uint)modifiers | NoRepeat, (uint)key);
            return _registered;
        }

        public void Dispose()
        {
            Apply(0, 0);
            _source.Dispose();
        }

        private nint OnMessage(nint handle, int message, nint wParam, nint lParam, ref bool handled)
        {
            if (message == HotkeyMessage && wParam == HotkeyId)
            {
                var item = ClipboardService.Read();
                if (item is not null)
                {
                    _store.Outgoing = item;
                }

                handled = true;
            }

            return 0;
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(nint handle, int id, uint modifiers, uint key);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(nint handle, int id);
    }
}
