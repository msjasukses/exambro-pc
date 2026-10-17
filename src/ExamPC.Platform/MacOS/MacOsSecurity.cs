using System.Diagnostics;
using System.Runtime.Versioning;
using ExamPC.Core.Security;

namespace ExamPC.Platform.MacOS;

/// <summary>
/// macOS sengaja tidak menyediakan padanan <c>WDA_EXCLUDEFROMCAPTURE</c> maupun
/// hook papan ketik global tanpa izin. Yang bisa dilakukan aplikasi biasa:
/// memeriksa lingkungan sebelum ujian, dan mencatat setiap kali jendela ujian
/// kehilangan fokus. Karena itu di macOS pengawasan langsung tetap wajib.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacOsSecurity : IPlatformSecurity
{
    public string PlatformName => $"macOS {Environment.OSVersion.Version}";

    /// <summary>
    /// <c>NSWindow.sharingType = .none</c> menyembunyikan jendela dari screen
    /// sharing, tetapi Cmd+Shift+3/4/5 tetap menangkapnya. Karena tidak menutup
    /// jalur yang paling sering dipakai, kemampuan ini dilaporkan tidak tersedia
    /// agar pengawas tidak merasa aman secara keliru.
    /// </summary>
    public bool SupportsScreenCaptureBlock => false;

    /// <summary>
    /// <c>CGEventTap</c> bisa menelan tombol, tetapi butuh izin Accessibility
    /// yang harus diberikan manual per perangkat — dan Cmd+Tab tetap lolos di
    /// macOS modern. Diperlakukan sebagai tidak tersedia.
    /// </summary>
    public bool SupportsHotkeyBlock => false;

    public string CapabilityNote =>
        "macOS tidak mengizinkan aplikasi biasa memblokir screenshot atau Cmd+Tab. " +
        "Andalkan mode fullscreen, deteksi kehilangan fokus, dan pengawasan langsung.";

    public CapabilityResult TryBlockScreenCapture(IntPtr windowHandle)
        => CapabilityResult.Unsupported(CapabilityNote);

    public CapabilityResult TryBlockSystemHotkeys()
        => CapabilityResult.Unsupported(CapabilityNote);

    public void ReleaseSystemHotkeys() { }

    /// <summary>Hitung layar lewat <c>system_profiler</c>: satu baris "Resolution" per layar.</summary>
    public int GetActiveDisplayCount()
    {
        var output = RunCommand("system_profiler", "SPDisplaysDataType");
        if (output is null) return 1;

        var count = output
            .Split('\n')
            .Count(line => line.TrimStart().StartsWith("Resolution:", StringComparison.Ordinal));

        return Math.Max(1, count);
    }

    /// <summary>Screen Sharing bawaan macOS dan VNC pihak ketiga sama-sama terlihat sebagai proses.</summary>
    public bool IsRemoteSession()
    {
        var names = GetRunningProcessNames();
        return names.Contains("screensharingd") ||
               names.Contains("vnc") ||
               names.Contains("ardagent");
    }

    public bool IsVirtualMachine()
    {
        var output = RunCommand("sysctl", "-n machdep.cpu.brand_string hw.model");
        if (output is null) return false;

        var text = output.ToLowerInvariant();
        return text.Contains("vmware") || text.Contains("virtualbox") ||
               text.Contains("parallels") || text.Contains("qemu") ||
               text.Contains("apple virtualization");
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

    public void ClearClipboard() => RunCommand("/bin/sh", "-c \"printf '' | pbcopy\"");

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

            // Perintah yang menggantung tidak boleh menahan layar penyiapan.
            if (!proc.WaitForExit(3000))
            {
                try { proc.Kill(entireProcessTree: true); } catch (Exception) { }
                return null;
            }

            return output;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
