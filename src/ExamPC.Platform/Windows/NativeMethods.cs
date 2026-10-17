using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ExamPC.Platform.Windows;

[SupportedOSPlatform("windows")]
internal static class NativeMethods
{
    // ---- Anti-screenshot --------------------------------------------------

    /// <summary>Jendela tetap terlihat mata, tapi kosong di hasil capture.</summary>
    internal const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011; // Windows 10 2004+
    internal const uint WDA_NONE = 0x00000000;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    // ---- Hook papan ketik tingkat rendah ----------------------------------

    internal const int WH_KEYBOARD_LL = 13;
    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_SYSKEYDOWN = 0x0104;
    internal const int WM_KEYUP = 0x0101;
    internal const int WM_SYSKEYUP = 0x0105;

    internal const int VK_TAB = 0x09;
    internal const int VK_ESCAPE = 0x1B;
    internal const int VK_LWIN = 0x5B;
    internal const int VK_RWIN = 0x5C;
    internal const int VK_F4 = 0x73;
    internal const int VK_SNAPSHOT = 0x2C; // PrintScreen
    internal const int VK_APPS = 0x5D;     // tombol menu konteks
    internal const int VK_SHIFT = 0x10;
    internal const int VK_LMENU = 0xA4;
    internal const int VK_RMENU = 0xA5;
    internal const int VK_LCONTROL = 0xA2;
    internal const int VK_RCONTROL = 0xA3;

    [StructLayout(LayoutKind.Sequential)]
    internal struct KBDLLHOOKSTRUCT
    {
        internal uint vkCode;
        internal uint scanCode;
        internal uint flags;
        internal uint time;
        internal IntPtr dwExtraInfo;
    }

    internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    // ---- Layar & sesi -----------------------------------------------------

    internal const int SM_CMONITORS = 80;
    internal const int SM_REMOTESESSION = 0x1000;

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int nIndex);

    // ---- Clipboard --------------------------------------------------------

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseClipboard();

    // ---- Taskbar ----------------------------------------------------------

    internal const int SW_HIDE = 0;
    internal const int SW_SHOW = 5;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);
}
