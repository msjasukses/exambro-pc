using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ExamPC.Core.Config;
using ExamPC.Core.Security;
using ExamPC.Platform;

namespace ExamPC.App.Views;

public partial class SetupWindow : Window
{
    private readonly IPlatformSecurity _platform = PlatformSecurityFactory.Create();
    private ExamConfig _config;
    private IReadOnlyList<SecurityFinding> _lastScan = Array.Empty<SecurityFinding>();
    private bool _scanned;

    public SetupWindow() : this(new ExamConfig()) { }

    public SetupWindow(ExamConfig config)
    {
        _config = config;
        InitializeComponent();
        PlatformLine.Text = $"{_platform.PlatformName} · konfigurasi tersimpan di {ConfigStore.ConfigPath}";
        LoadIntoForm();
    }

    // ---- Form <-> konfigurasi ---------------------------------------------

    private void LoadIntoForm()
    {
        UrlBox.Text = _config.ExamUrl;
        RoomBox.Text = _config.RoomLabel;
        WebhookBox.Text = _config.ViolationWebhookUrl;
    }

    /// <summary>Kumpulkan isi form; null bila ada yang belum sah.</summary>
    private ExamConfig? ReadForm()
    {
        var url = (UrlBox.Text ?? "").Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            SetStatus("URL ujian harus diawali http:// atau https://", isError: true);
            UrlBox.Focus();
            return null;
        }

        return new ExamConfig
        {
            ExamUrl = url,
            // Isian domain tambahan sudah dihapus dari form. Sengaja dikosongkan,
            // bukan mewarisi isi lama, supaya tidak ada daftar putih tersembunyi
            // yang tidak bisa dilihat atau diubah pengawas.
            ExtraAllowedHosts = new List<string>(),
            RoomLabel = (RoomBox.Text ?? "").Trim(),
            ViolationWebhookUrl = (WebhookBox.Text ?? "").Trim(),

            // Seluruh proteksi selalu aktif dan tidak bisa dimatikan dari form —
            // termasuk untuk konfigurasi lama yang dulu menyimpan sakelar mati.
            // Pengecualiannya hanya kemampuan yang memang tidak ada di OS ini:
            // menyalakannya cuma menambah catatan "gagal" di log setiap sesi.
            BlockScreenCapture = _platform.SupportsScreenCaptureBlock,
            BlockSystemHotkeys = _platform.SupportsHotkeyBlock,
            BlockClipboard = true,
            RequireSingleDisplay = true,
            DetectForbiddenProcesses = true,
            BlockVirtualMachine = true,
            LockOnFocusLoss = true,

            // PIN keluar kini diperiksa ke server (lihat ExitPinVerifier), dan batas
            // kehilangan fokus tidak lagi bisa diatur: keduanya memakai nilai bawaan.
        };
    }

    // ---- Pemindaian --------------------------------------------------------

    private void OnScanClick(object? sender, RoutedEventArgs e)
    {
        var config = ReadForm();
        if (config is null) return;

        _config = config;
        ConfigStore.Save(_config);

        ScanButton.IsEnabled = false;
        SetStatus("Memeriksa perangkat…", isError: false);

        // Enumerasi proses dan WMI memakan waktu; jangan bekukan antarmuka.
        Task.Run(() => new PreflightScanner(_platform).Scan(_config))
            .ContinueWith(t =>
            {
                _lastScan = t.IsCompletedSuccessfully ? t.Result : Array.Empty<SecurityFinding>();
                _scanned = t.IsCompletedSuccessfully;

                RenderScan(_lastScan);
                ScanButton.IsEnabled = true;

                if (!t.IsCompletedSuccessfully)
                    SetStatus($"Pemindaian gagal: {t.Exception?.GetBaseException().Message}", isError: true);
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void RenderScan(IReadOnlyList<SecurityFinding> findings)
    {
        ScanList.Children.Clear();

        if (findings.Count == 0)
        {
            ScanList.Children.Add(Card(
                "Perangkat siap",
                "Tidak ditemukan temuan apa pun. Ujian boleh dimulai.",
                "",
                Severity.Info));
            SetStatus("Perangkat bersih — silakan mulai ujian.", isError: false);
            return;
        }

        foreach (var f in findings.OrderByDescending(f => f.Severity))
            ScanList.Children.Add(Card(f.Title, f.Detail, f.Remedy, f.Severity));

        var blockers = findings.Count(f => f.Severity == Severity.Block);
        SetStatus(
            blockers > 0
                ? $"{blockers} temuan memblokir ujian. Bereskan dulu, lalu periksa ulang."
                : $"{findings.Count} peringatan. Ujian boleh dimulai, tetapi perhatikan catatan di atas.",
            isError: blockers > 0);
    }

    private static Border Card(string title, string detail, string remedy, Severity severity)
    {
        var (bg, accent, tag) = severity switch
        {
            Severity.Block => ("#FCEBEC", "#B4232B", "BLOKIR"),
            Severity.Warn => ("#FFF6E0", "#8A6100", "PERINGATAN"),
            _ => ("#E7F5EC", "#1F6F43", "AMAN"),
        };

        var stack = new StackPanel { Spacing = 3 };

        stack.Children.Add(new TextBlock
        {
            Text = $"{tag} · {title}",
            FontWeight = FontWeight.SemiBold,
            Foreground = SolidColorBrush.Parse(accent),
            TextWrapping = TextWrapping.Wrap,
        });

        if (detail.Length > 0)
            stack.Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap });

        if (remedy.Length > 0)
            stack.Children.Add(new TextBlock
            {
                Text = "→ " + remedy,
                FontSize = 12,
                Foreground = SolidColorBrush.Parse("#5A6673"),
                TextWrapping = TextWrapping.Wrap,
            });

        return new Border
        {
            Background = SolidColorBrush.Parse(bg),
            BorderBrush = SolidColorBrush.Parse(accent),
            BorderThickness = new Avalonia.Thickness(3, 0, 0, 0),
            CornerRadius = new Avalonia.CornerRadius(4),
            Padding = new Avalonia.Thickness(12, 10),
            Child = stack,
        };
    }

    // ---- Mulai ujian -------------------------------------------------------

    private void OnStartClick(object? sender, RoutedEventArgs e)
    {
        var config = ReadForm();
        if (config is null) return;

        _config = config;
        ConfigStore.Save(_config);

        if (!_scanned)
        {
            SetStatus("Jalankan Periksa Perangkat lebih dulu — ujian tidak boleh dimulai tanpa pemeriksaan.", isError: true);
            return;
        }

        if (!PreflightScanner.CanStart(_lastScan))
        {
            SetStatus("Masih ada temuan berkategori BLOKIR. Bereskan lalu periksa ulang.", isError: true);
            return;
        }

        var exam = new ExamWindow(_config, _platform);
        exam.Show();
        Close();
    }

    private void SetStatus(string message, bool isError)
    {
        StatusLine.Text = message;
        StatusLine.Foreground = isError
            ? SolidColorBrush.Parse("#B4232B")
            : SolidColorBrush.Parse("#5A6673");
    }
}
