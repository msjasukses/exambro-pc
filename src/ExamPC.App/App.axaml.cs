using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ExamPC.App.Views;
using ExamPC.Core.Config;

namespace ExamPC.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Selalu mulai dari layar penyiapan pengawas. Tidak ada jalur yang
            // membuka jendela ujian langsung dari baris perintah — kalau ada,
            // siswa bisa memancing sesi tanpa PIN pengawas.
            desktop.MainWindow = new SetupWindow(ConfigStore.Load());
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => Recovery.RestoreDesktop();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
