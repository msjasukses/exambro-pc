using ExamPC.Core.Config;

namespace ExamPC.Core.Security;

/// <summary>
/// Pemindaian pra-ujian: kumpulkan semua alasan mengapa perangkat ini belum
/// layak dipakai ujian, sebelum siswa terlanjur masuk ke soal.
/// </summary>
public sealed class PreflightScanner(IPlatformSecurity platform)
{
    public IReadOnlyList<SecurityFinding> Scan(ExamConfig config)
    {
        var findings = new List<SecurityFinding>();

        CheckExamUrl(config, findings);
        CheckRemoteSession(findings);
        if (config.RequireSingleDisplay) CheckDisplays(findings);
        if (config.BlockVirtualMachine) CheckVirtualMachine(findings);
        if (config.DetectForbiddenProcesses) CheckProcesses(findings);
        CheckCapabilities(config, findings);

        return findings;
    }

    /// <summary>Ujian ditolak selama masih ada temuan berkategori BLOKIR.</summary>
    public static bool CanStart(IEnumerable<SecurityFinding> findings)
        => findings.All(f => f.Severity != Severity.Block);

    private static void CheckExamUrl(ExamConfig config, List<SecurityFinding> findings)
    {
        if (!Uri.TryCreate(config.ExamUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            findings.Add(new SecurityFinding(
                "INVALID_URL", Severity.Block,
                "URL ujian tidak sah",
                $"'{config.ExamUrl}' bukan alamat http/https yang valid.",
                "Perbaiki URL ujian di layar penyiapan."));
            return;
        }

        if (uri.Scheme == "http")
        {
            findings.Add(new SecurityFinding(
                "INSECURE_URL", Severity.Warn,
                "Ujian dilayani lewat HTTP",
                $"{uri.Host} diakses tanpa enkripsi; jawaban siswa bisa disadap di jaringan lab.",
                "Gunakan HTTPS pada server ujian bila memungkinkan."));
        }
    }

    private void CheckRemoteSession(List<SecurityFinding> findings)
    {
        if (platform.IsRemoteSession())
        {
            findings.Add(new SecurityFinding(
                "REMOTE_SESSION", Severity.Block,
                "Sesi remote desktop terdeteksi",
                "Layar ini sedang dikendalikan dari komputer lain, sehingga soal bisa dikerjakan orang lain.",
                "Jalankan aplikasi langsung di perangkat siswa, bukan lewat RDP/VNC."));
        }
    }

    private void CheckDisplays(List<SecurityFinding> findings)
    {
        var count = platform.GetActiveDisplayCount();
        if (count > 1)
        {
            findings.Add(new SecurityFinding(
                "MULTI_DISPLAY", Severity.Block,
                "Lebih dari satu layar aktif",
                $"Terdeteksi {count} layar. Soal bisa disorot atau digandakan ke layar kedua.",
                "Cabut proyektor/monitor tambahan, atau matikan opsi layar tunggal bila memang disengaja."));
        }
    }

    private void CheckVirtualMachine(List<SecurityFinding> findings)
    {
        if (platform.IsVirtualMachine())
        {
            findings.Add(new SecurityFinding(
                "VIRTUAL_MACHINE", Severity.Block,
                "Berjalan di mesin virtual",
                "Di dalam VM, proteksi anti-screenshot dan kunci tombol bisa dilewati dari sistem induk.",
                "Jalankan pada Windows/macOS/Linux fisik di perangkat siswa."));
        }
    }

    private void CheckProcesses(List<SecurityFinding> findings)
    {
        var matches = ProcessBlacklist.Match(platform.GetRunningProcessNames());
        if (matches.Count == 0) return;

        foreach (var group in matches.GroupBy(m => m.Category))
        {
            var names = string.Join(", ", group.Select(g => g.DisplayName).Distinct());

            // Remote desktop, perekam layar, dan VM benar-benar membatalkan ujian;
            // sisanya cukup diketahui pengawas.
            var severity = group.Key is "Remote desktop" or "Perekam layar" or "Mesin virtual" or "Otomasi"
                ? Severity.Block
                : Severity.Warn;

            findings.Add(new SecurityFinding(
                "PROCESS_" + group.Key.ToUpperInvariant().Replace(' ', '_'),
                severity,
                $"{group.Key} sedang berjalan",
                names,
                $"Tutup {names} lewat Task Manager sebelum ujian dimulai."));
        }
    }

    private void CheckCapabilities(ExamConfig config, List<SecurityFinding> findings)
    {
        if (config.BlockScreenCapture && !platform.SupportsScreenCaptureBlock)
        {
            findings.Add(new SecurityFinding(
                "NO_CAPTURE_BLOCK", Severity.Warn,
                "Anti-screenshot tidak tersedia",
                $"{platform.PlatformName}: {platform.CapabilityNote}",
                "Awasi siswa secara langsung, atau gunakan perangkat Windows 10 versi 2004 ke atas."));
        }

        if (config.BlockSystemHotkeys && !platform.SupportsHotkeyBlock)
        {
            findings.Add(new SecurityFinding(
                "NO_HOTKEY_BLOCK", Severity.Warn,
                "Kunci tombol sistem tidak tersedia",
                $"{platform.PlatformName}: {platform.CapabilityNote}",
                "Andalkan deteksi kehilangan fokus; setiap perpindahan aplikasi tetap tercatat."));
        }
    }
}
