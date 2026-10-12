using System.Runtime.InteropServices;

namespace ClipboardPhonePc
{
    public static class QuietState
    {
        private const ulong FocusAssistStateName = 0x0D83063EA3BF1C75;
        private const int AcceptsNotifications = 5;
        private const int AppRunning = 7;

        public static bool IsActive => IsFocusAssistOn() || IsShellBusy();

        private static bool IsFocusAssistOn()
        {
            var stateName = FocusAssistStateName;
            var size = (uint)sizeof(int);
            var status = NtQueryWnfStateData(ref stateName, 0, 0, out _, out var profile, ref size);
            return status == 0 && profile > 0;
        }

        private static bool IsShellBusy()
        {
            return SHQueryUserNotificationState(out var state) == 0
                && state != AcceptsNotifications
                && state != AppRunning;
        }

        [DllImport("ntdll.dll")]
        private static extern int NtQueryWnfStateData(ref ulong stateName, nint typeId, nint explicitScope, out uint changeStamp, out int buffer, ref uint bufferSize);

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out int state);
    }
}
