namespace ExamPC.Core.Security;

/// <summary>
/// Semua hal yang jawabannya berbeda per sistem operasi. Windows mengisi ini
/// paling lengkap; macOS dan Linux mengembalikan <c>Unsupported</c> untuk
/// kemampuan yang memang tidak ada padanannya, dan <see cref="PreflightScanner"/>
/// menurunkan temuan yang bersangkutan menjadi peringatan alih-alih pemblokir.
/// </summary>
public interface IPlatformSecurity
{
    string PlatformName { get; }

    /// <summary>
    /// Apakah OS ini punya mekanisme menyembunyikan jendela dari screenshot.
    /// Ditanyakan saat pemindaian pra-ujian, jadi tidak boleh mengubah keadaan apa pun.
    /// </summary>
    bool SupportsScreenCaptureBlock { get; }

    /// <summary>Apakah OS ini mengizinkan hook tombol sistem global.</summary>
    bool SupportsHotkeyBlock { get; }

    /// <summary>Alasan kemampuan di atas tidak tersedia, untuk ditampilkan ke pengawas.</summary>
    string CapabilityNote { get; }

    /// <summary>Sembunyikan jendela dari screenshot dan screen-share.</summary>
    /// <param name="windowHandle">HWND (Windows) / NSWindow* (macOS) / XID (Linux).</param>
    CapabilityResult TryBlockScreenCapture(IntPtr windowHandle);

    /// <summary>Pasang hook yang menelan Alt+Tab, tombol Windows/Command, Alt+F4, PrintScreen.</summary>
    CapabilityResult TryBlockSystemHotkeys();

    /// <summary>Lepas hook tombol sistem. Selalu dipanggil saat sesi berakhir.</summary>
    void ReleaseSystemHotkeys();

    /// <summary>Jumlah layar aktif. Lebih dari satu berarti materi ujian bisa disorot ke luar.</summary>
    int GetActiveDisplayCount();

    /// <summary>Apakah sesi ini berjalan lewat remote desktop (RDP, VNC, dan sejenisnya).</summary>
    bool IsRemoteSession();

    /// <summary>Apakah OS berjalan di dalam mesin virtual.</summary>
    bool IsVirtualMachine();

    /// <summary>Nama proses yang sedang berjalan, huruf kecil, tanpa ekstensi.</summary>
    IReadOnlyList<string> GetRunningProcessNames();

    /// <summary>Kosongkan clipboard sistem agar soal tidak bisa dibawa keluar.</summary>
    void ClearClipboard();
}

/// <param name="Supported">False bila OS ini memang tidak menyediakan kemampuannya.</param>
/// <param name="Applied">True bila proteksi benar-benar aktif sekarang.</param>
/// <param name="Note">Alasan gagal atau catatan untuk pengawas.</param>
public readonly record struct CapabilityResult(bool Supported, bool Applied, string Note = "")
{
    public static CapabilityResult Ok() => new(true, true);
    public static CapabilityResult Failed(string note) => new(true, false, note);
    public static CapabilityResult Unsupported(string note) => new(false, false, note);
}
