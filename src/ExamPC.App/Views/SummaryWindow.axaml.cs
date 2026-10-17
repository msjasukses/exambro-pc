using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ExamPC.Core.Config;
using ExamPC.Core.Security;

namespace ExamPC.App.Views;

/// <summary>
/// Ditampilkan setelah pengawas mengakhiri sesi. Ini satu-satunya kesempatan
/// pengawas melihat seluruh pelanggaran sekaligus tanpa membuka berkas log.
/// </summary>
public partial class SummaryWindow : Window
{
    public sealed record Row(string Time, string Code, string Detail);

    public SummaryWindow() : this(null, DateTimeOffset.Now, "") { }

    public SummaryWindow(ViolationLog? log, DateTimeOffset startedAt, string room)
    {
        InitializeComponent();

        var duration = (DateTimeOffset.Now - startedAt).ToString(@"hh\:mm\:ss");
        var roomPart = string.IsNullOrWhiteSpace(room) ? "" : $"{room} · ";
        MetaLine.Text = $"{roomPart}mulai {startedAt:HH:mm} · berlangsung {duration} · {Environment.MachineName}";

        var violations = log?.All ?? Array.Empty<Violation>();

        if (violations.Count == 0)
        {
            HeadlineLine.Text = "Tidak ada pelanggaran tercatat sepanjang sesi.";
            HeadlineLine.Foreground = SolidColorBrush.Parse("#1F6F43");
        }
        else
        {
            HeadlineLine.Text = $"{violations.Count} pelanggaran tercatat.";
            HeadlineLine.Foreground = SolidColorBrush.Parse("#B4232B");

            ViolationList.ItemsSource = violations
                .OrderByDescending(v => v.At)
                .Select(v => new Row(v.At.ToString("HH:mm:ss"), v.Code, v.Detail))
                .ToList();
        }

        LogPathLine.Text = log is null ? "" : $"Log lengkap: {log.LogPath}";
    }

    private void OnNewSession(object? sender, RoutedEventArgs e)
    {
        new SetupWindow(ConfigStore.Load()).Show();
        Close();
    }

    private void OnQuit(object? sender, RoutedEventArgs e)
    {
        Recovery.RestoreDesktop();

        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            Close();
    }
}
