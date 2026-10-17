using System.Text.Json.Serialization;

namespace ExamPC.Core.Config;

/// <summary>
/// Seluruh pengaturan sesi ujian yang diisi pengawas di layar penyiapan.
/// Disimpan terenkripsi lewat <see cref="ConfigStore"/>.
/// </summary>
public sealed class ExamConfig
{
    /// <summary>URL halaman ujian. Host-nya otomatis masuk white-list.</summary>
    public string ExamUrl { get; set; } = "";

    /// <summary>
    /// Domain tambahan yang boleh dimuat, satu per baris. Mendukung wildcard
    /// awalan, mis. <c>*.sekolah.sch.id</c> atau <c>cdn.jsdelivr.net</c>.
    /// </summary>
    public List<string> ExtraAllowedHosts { get; set; } = new();

    // ---- Sakelar proteksi -------------------------------------------------

    /// <summary>Sembunyikan jendela dari screenshot &amp; screen-share.</summary>
    public bool BlockScreenCapture { get; set; } = true;

    /// <summary>Pasang low-level keyboard hook (Alt+Tab, Win, Alt+F4, PrtSc).</summary>
    public bool BlockSystemHotkeys { get; set; } = true;

    /// <summary>Matikan seleksi teks, klik kanan, copy/paste, dan cetak.</summary>
    public bool BlockClipboard { get; set; } = true;

    /// <summary>Tolak mulai ujian bila terpasang lebih dari satu monitor aktif.</summary>
    public bool RequireSingleDisplay { get; set; } = true;

    /// <summary>Pindai aplikasi remote-desktop &amp; perekam layar.</summary>
    public bool DetectForbiddenProcesses { get; set; } = true;

    /// <summary>Tolak mulai ujian di mesin virtual.</summary>
    public bool BlockVirtualMachine { get; set; } = true;

    /// <summary>Kunci layar saat jendela ujian kehilangan fokus.</summary>
    public bool LockOnFocusLoss { get; set; } = true;

    /// <summary>
    /// Berapa kali siswa boleh kehilangan fokus sebelum sesi dibekukan dan
    /// hanya bisa dilanjutkan pengawas. 0 = langsung beku pada pelanggaran pertama.
    /// </summary>
    public int MaxFocusLossBeforeFreeze { get; set; } = 3;

    /// <summary>
    /// Endpoint opsional untuk mengirim log pelanggaran (POST JSON).
    /// Kosongkan bila log cukup disimpan lokal.
    /// </summary>
    public string ViolationWebhookUrl { get; set; } = "";

    /// <summary>Identitas ruang/sesi yang ikut dikirim bersama log pelanggaran.</summary>
    public string RoomLabel { get; set; } = "";

    [JsonIgnore]
    public bool IsReady => !string.IsNullOrWhiteSpace(ExamUrl);
}
