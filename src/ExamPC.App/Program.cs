using System.Runtime.CompilerServices;
using Avalonia;
using ExamPC.Core.Config;
using Xilium.CefGlue;
using Xilium.CefGlue.Common;

namespace ExamPC.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Dipasang paling awal: kegagalan memuat rakitan CEF muncul sebagai
        // pengecualian di thread lain, di luar jangkauan try/catch di bawah.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) WriteCrashLog(ex);
        };

        try
        {
            ConfigureCef();
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // Aplikasi ujian yang mati tanpa jejak tidak bisa didiagnosis oleh
            // teknisi lab — mereka hanya melihat jendela yang tidak pernah muncul.
            // Tulis penyebabnya ke berkas yang bisa dikirim ke pengembang.
            WriteCrashLog(ex);
            return 1;
        }
        finally
        {
            // Taskbar harus kembali walau aplikasi berakhir tidak wajar.
            Recovery.RestoreDesktop();
        }
    }

    /// <summary>
    /// Menyalakan runtime CEF sebelum jendela mana pun dibuat.
    ///
    /// Wajib berada di metode terpisah yang tidak di-inline: bila pemanggilan ini
    /// ada langsung di <see cref="Main"/>, JIT memuat rakitan CEF sebelum blok
    /// try/catch terpasang, dan kegagalan pemuatan menjatuhkan proses tanpa jejak
    /// apa pun — persis kegagalan yang sebelumnya menghabiskan waktu untuk dilacak.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ConfigureCef()
    {
        var settings = new CefSettings
        {
            // Tidak ada profil yang menetap: cookie, riwayat, dan cache ikut hilang
            // begitu aplikasi ditutup, sehingga jawaban siswa sebelumnya tidak bisa
            // ditemukan oleh siswa di sesi berikutnya pada PC yang sama.
            CachePath = null,
            PersistSessionCookies = false,

            // Jangan tinggalkan berkas log di folder aplikasi — isinya membocorkan
            // URL ujian dan bisa dibaca siapa pun yang membuka Program Files.
            LogSeverity = CefLogSeverity.Disable,
            LogFile = null,

            // CEF tidak boleh mendaftarkan penanganan skema atau plugin tambahan.
            NoSandbox = false,
        };

        // Argumen tingkat Chromium. Yang tidak bisa dimatikan lewat CefSettings
        // dimatikan di sini.
        CefRuntimeLoader.Initialize(settings, new[]
        {
            new KeyValuePair<string, string>("disable-pdf-extension", "1"), // viewer PDF punya tombol unduh & cetak sendiri
            new KeyValuePair<string, string>("disable-plugins", "1"),
            new KeyValuePair<string, string>("disable-background-networking", "1"),
        });
    }

    private static void WriteCrashLog(Exception ex)
    {
        try
        {
            var path = Path.Combine(ConfigStore.DataDirectory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(path,
                $"ExamPC crash{Environment.NewLine}" +
                $"Waktu   : {DateTimeOffset.Now:O}{Environment.NewLine}" +
                $"Mesin   : {Environment.MachineName}{Environment.NewLine}" +
                $"OS      : {Environment.OSVersion}{Environment.NewLine}" +
                $"64-bit  : {Environment.Is64BitProcess}{Environment.NewLine}{Environment.NewLine}" +
                ex.ToString());

            Console.Error.WriteLine($"ExamPC gagal dijalankan. Rincian: {path}");
            Console.Error.WriteLine(ex.ToString());
        }
        catch (Exception)
        {
            // Sudah di jalur penanganan crash; tidak ada lagi yang bisa dilakukan.
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
