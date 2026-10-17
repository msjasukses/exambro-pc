using ExamPC.Platform.Windows;

namespace ExamPC.App;

/// <summary>
/// Jaring pengaman terakhir. Setiap proteksi yang mengubah keadaan sistem di
/// luar jendela kita — taskbar yang disembunyikan, hook papan ketik — harus
/// bisa dipulihkan meski aplikasi berakhir tidak wajar.
/// </summary>
public static class Recovery
{
    public static void RestoreDesktop()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            using var win = new WindowsSecurity();
            win.ShowTaskbar();
            win.ReleaseSystemHotkeys();
        }
        catch (Exception)
        {
            // Sudah di jalur pemulihan darurat; tidak ada lagi yang bisa dilakukan.
        }
    }
}
