using System.Diagnostics;
using System.Runtime.Versioning;
using ExamPC.Core.Security;

namespace ExamPC.Platform.Linux;

/// <summary>
/// Di Linux, seberapa kuat proteksi bergantung pada server tampilan. Wayland
/// mengisolasi aplikasi dengan baik — justru karena itu aplikasi kita pun tidak
/// bisa memblokir screenshot atau menelan tombol milik compositor. X11 lebih
/// longgar, tapi sama-sama tidak punya padanan WDA_EXCLUDEFROMCAPTURE.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxSecurity : IPlatformSecurity
{
    private static string SessionType =>
        Environment.GetEnvironmentVariable("XDG_SESSION_TYPE")?.ToLowerInvariant() ?? "unknown";

    public string PlatformName => $"Linux ({SessionType})";

    public bool SupportsScreenCaptureBlock => false;

    public bool SupportsHotkeyBlock => false;

    public string CapabilityNote => SessionType == "wayland"
        ? "Wayland mengisolasi aplikasi, sehingga ExamPC tidak bisa memblokir screenshot " +
          "atau pintasan compositor. Kunci pintasan Super/Alt+Tab lewat pengaturan desktop lab."
        : "X11 tidak menyediakan pemblokiran screenshot tingkat jendela. " +
          "Kunci pintasan lewat pengaturan window manager, dan andalkan deteksi kehilangan fokus.";

    public CapabilityResult TryBlockScreenCapture(IntPtr windowHandle)
        => CapabilityResult.Unsupported(CapabilityNote);

    public CapabilityResult TryBlockSystemHotkeys()
        => CapabilityResult.Unsupported(CapabilityNote);

    public void ReleaseSystemHotkeys() { }

    /// <summary>Hitung output yang berstatus "connected" di <c>xrandr</c>.</summary>
    public int GetActiveDisplayCount()
    {
        var output = RunCommand("xrandr", "--listmonitors");
        if (output is not null)
        {
            // Baris pertama berbentuk "Monitors: N".
            var first = output.Split('\n').FirstOrDefault() ?? "";
            var parts = first.Split(':', 2);
            if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out var n))
                return Math.Max(1, n);
        }

        // Wayland: xrandr sering tidak tersedia. Jumlah layar nanti dibaca dari
        // lapis UI (Avalonia Screens) sebagai gantinya.
        return 1;
    }

    public bool IsRemoteSession()
    {
        // Sesi SSH dengan X-forwarding, atau server VNC/RDP yang sedang berjalan.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SSH_CONNECTION")))
            return true;

        var names = GetRunningProcessNames();
        return names.Contains("x11vnc") || names.Contains("vncserver") ||
               names.Contains("xrdp") || names.Contains("tigervncserver");
    }

    public bool IsVirtualMachine()
    {
        // systemd-detect-virt adalah jawaban paling akurat bila tersedia.
        var virt = RunCommand("systemd-detect-virt", "")?.Trim();
        if (!string.IsNullOrEmpty(virt) && virt != "none") return true;

        try
        {
            const string dmiPath = "/sys/class/dmi/id/product_name";
            if (File.Exists(dmiPath))
            {
                var product = File.ReadAllText(dmiPath).ToLowerInvariant();
                if (product.Contains("virtualbox") || product.Contains("vmware") ||
                    product.Contains("kvm") || product.Contains("qemu") ||
                    product.Contains("virtual machine"))
                {
                    return true;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // DMI tidak terbaca di container atau kernel terkunci; abaikan.
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
        // xclip untuk X11, wl-copy untuk Wayland — pakai yang ada.
        RunCommand("/bin/sh", "-c \"printf '' | xclip -selection clipboard\"");
        RunCommand("/bin/sh", "-c \"printf '' | wl-copy\"");
    }

    internal static string? RunCommand(string file, string args)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (proc is null) return null;

            var output = proc.StandardOutput.ReadToEnd();

            if (!proc.WaitForExit(3000))
            {
                try { proc.Kill(entireProcessTree: true); } catch (Exception) { }
                return null;
            }

            return output;
        }
        catch (Exception)
        {
            // Perintah tidak terpasang di distro ini — bukan alasan menggagalkan ujian.
            return null;
        }
    }
}
