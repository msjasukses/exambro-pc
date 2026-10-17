using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ExamPC.Core.Security;
using static ExamPC.Platform.Windows.NativeMethods;

namespace ExamPC.Platform.Windows;

/// <summary>
/// Implementasi proteksi untuk Windows 10/11 — platform dengan cakupan terlengkap.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSecurity : IPlatformSecurity, IDisposable
{
    private IntPtr _hookHandle = IntPtr.Zero;

    // Delegat wajib disimpan di field: bila hanya jadi variabel lokal, GC
    // membebaskannya sementara Windows masih memegang pointer-nya, dan aplikasi
    // menutup diri secara acak beberapa menit setelah hook terpasang.
    private LowLevelKeyboardProc? _hookProc;

    /// <summary>Dipanggil setiap kali hook menelan sebuah tombol, untuk dicatat sebagai pelanggaran.</summary>
    public event Action<string>? HotkeyBlocked;

    public string PlatformName => $"Windows {Environment.OSVersion.Version}";

    /// <summary>
    /// WDA_EXCLUDEFROMCAPTURE baru ada di Windows 10 versi 2004 (build 19041).
    /// Di bawah itu panggilannya tetap berhasil, tetapi hasilnya hanya jendela
    /// hitam bagi dirinya sendiri — bukan proteksi yang bisa diandalkan.
    /// </summary>
    public bool SupportsScreenCaptureBlock => Environment.OSVersion.Version.Build >= 19041;

    public bool SupportsHotkeyBlock => true;

    public string CapabilityNote => SupportsScreenCaptureBlock
        ? ""
        : "Anti-screenshot penuh butuh Windows 10 versi 2004 (build 19041) ke atas.";

    // ---- Anti-screenshot --------------------------------------------------

    public CapabilityResult TryBlockScreenCapture(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
            return CapabilityResult.Failed("Handle jendela belum tersedia.");

        if (!SupportsScreenCaptureBlock)
            return CapabilityResult.Unsupported(CapabilityNote);

        return SetWindowDisplayAffinity(windowHandle, WDA_EXCLUDEFROMCAPTURE)
            ? CapabilityResult.Ok()
            : CapabilityResult.Failed(
                $"SetWindowDisplayAffinity gagal (kode {Marshal.GetLastWin32Error()}).");
    }

    public void ReleaseScreenCaptureBlock(IntPtr windowHandle)
    {
        if (windowHandle != IntPtr.Zero)
            SetWindowDisplayAffinity(windowHandle, WDA_NONE);
    }

    // ---- Hook papan ketik -------------------------------------------------

    public CapabilityResult TryBlockSystemHotkeys()
    {
        if (_hookHandle != IntPtr.Zero) return CapabilityResult.Ok();

        _hookProc = HookCallback;
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);

        if (_hookHandle == IntPtr.Zero)
        {
            _hookProc = null;
            return CapabilityResult.Failed(
                $"SetWindowsHookEx gagal (kode {Marshal.GetLastWin32Error()}). " +
                "Hook tingkat rendah butuh antrean pesan Windows di thread UI.");
        }

        return CapabilityResult.Ok();
    }

    public void ReleaseSystemHotkeys()
    {
        if (_hookHandle == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
        _hookProc = null;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        var message = (int)wParam;
        if (message is WM_KEYDOWN or WM_SYSKEYDOWN)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var blocked = Classify((int)info.vkCode);

            if (blocked is not null)
            {
                HotkeyBlocked?.Invoke(blocked);
                return 1; // telan tombol: jangan diteruskan ke aplikasi mana pun
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
    private static bool AltDown => IsDown(VK_LMENU) || IsDown(VK_RMENU);
    private static bool CtrlDown => IsDown(VK_LCONTROL) || IsDown(VK_RCONTROL);
    private static bool ShiftDown => IsDown(VK_SHIFT);

    /// <summary>Nama pintasan yang ditelan, atau null bila tombol boleh diteruskan.</summary>
    private static string? Classify(int vkCode)
    {
        switch (vkCode)
        {
            case VK_LWIN:
            case VK_RWIN:
                return "Tombol Windows";

            case VK_APPS:
                return "Tombol menu konteks";

            case VK_SNAPSHOT:
                return "PrintScreen";

            case VK_TAB when AltDown:
                return "Alt+Tab";

            case VK_F4 when AltDown:
                return "Alt+F4";

            // Ctrl+Shift+Esc membuka Task Manager. Hook tingkat rendah masih
            // sempat menelannya; hanya Ctrl+Alt+Del (Secure Attention Sequence)
            // yang berada di luar jangkauan aplikasi biasa.
            case VK_ESCAPE when CtrlDown && ShiftDown:
                return "Ctrl+Shift+Esc";

            case VK_ESCAPE when CtrlDown:
                return "Ctrl+Esc";

            case VK_ESCAPE when AltDown:
                return "Alt+Esc";

            default:
                return null;
        }
    }

    // ---- Pemeriksaan lingkungan -------------------------------------------

    public int GetActiveDisplayCount() => Math.Max(1, GetSystemMetrics(SM_CMONITORS));

    public bool IsRemoteSession() => GetSystemMetrics(SM_REMOTESESSION) != 0;

    private static readonly string[] VmMarkers =
    {
        "vmware", "virtualbox", "kvm", "qemu", "xen", "bochs",
        "virtual machine", "hvm domu", "parallels", "innotek",
    };

    public bool IsVirtualMachine()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Manufacturer, Model FROM Win32_ComputerSystem");

            foreach (var o in searcher.Get())
            {
                var text = $"{o["Manufacturer"]} {o["Model"]}".ToLowerInvariant();
                if (VmMarkers.Any(m => text.Contains(m, StringComparison.Ordinal)))
                    return true;
            }
        }
        catch (ManagementException)
        {
            // WMI dimatikan administrator: jangan sampai ujian gagal dimulai
            // karena ini. Deteksi proses VM tetap jadi jaring pengaman.
        }

        return false;
    }

    public IReadOnlyList<string> GetRunningProcessNames()
    {
        try
        {
            return Process.GetProcesses()
                .Select(p =>
                {
                    // Proses bisa mati tepat di antara enumerasi dan pembacaan nama.
                    try { return p.ProcessName.ToLowerInvariant(); }
                    catch (InvalidOperationException) { return ""; }
                })
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    public void ClearClipboard()
    {
        if (!OpenClipboard(IntPtr.Zero)) return;
        try { EmptyClipboard(); }
        finally { CloseClipboard(); }
    }

    // ---- Taskbar ----------------------------------------------------------

    /// <summary>
    /// Sembunyikan taskbar agar tombol Start dan daftar aplikasi tidak terjangkau.
    /// Wajib dipulihkan lewat <see cref="ShowTaskbar"/> saat sesi berakhir — bila
    /// aplikasi mati mendadak, siswa akan menemukan desktop tanpa taskbar.
    /// </summary>
    public void HideTaskbar() => SetTaskbarVisible(false);

    public void ShowTaskbar() => SetTaskbarVisible(true);

    private static void SetTaskbarVisible(bool visible)
    {
        var command = visible ? SW_SHOW : SW_HIDE;

        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar != IntPtr.Zero) ShowWindow(taskbar, command);

        var startButton = FindWindow("Button", "Start");
        if (startButton != IntPtr.Zero) ShowWindow(startButton, command);
    }

    /// <summary>Tarik kembali jendela ujian ke depan bila sesuatu merebut fokus.</summary>
    public bool ForceForeground(IntPtr windowHandle)
        => windowHandle != IntPtr.Zero &&
           GetForegroundWindow() != windowHandle &&
           SetForegroundWindow(windowHandle);

    public void Dispose()
    {
        ReleaseSystemHotkeys();
        ShowTaskbar(); // jangan pernah tinggalkan mesin lab tanpa taskbar
    }
}
