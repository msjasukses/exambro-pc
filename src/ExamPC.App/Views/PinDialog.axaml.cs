using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace ExamPC.App.Views;

/// <summary>Hasil pemeriksaan PIN: diterima, atau ditolak beserta pesan untuk pengawas.</summary>
public sealed record PinCheck(bool Accepted, string Message)
{
    public static readonly PinCheck Accept = new(true, "");
    public static PinCheck Reject(string message) => new(false, message);
}

/// <summary>
/// Meminta PIN pengawas. Mengembalikan true bila PIN diterima, false bila dibatalkan.
///
/// Verifikasi dilakukan pemanggil lewat <c>verify</c> — dialog ini sengaja tidak
/// memegang kunci API maupun PIN cadangan. Dialog tetap terbuka selama PIN diperiksa
/// ke server, dan bila PIN salah, agar pengawas bisa langsung mencoba lagi.
/// </summary>
public partial class PinDialog : Window
{
    private readonly Func<string, Task<PinCheck>> _verify;
    private bool _checking;
    private bool _closed;

    // Hanya dipakai perancang XAML.
    public PinDialog() : this("Konfirmasi Pengawas", _ => Task.FromResult(PinCheck.Reject(""))) { }

    public PinDialog(string title, Func<string, Task<PinCheck>> verify)
    {
        _verify = verify;
        InitializeComponent();
        TitleLine.Text = title;
        Title = title;

        Opened += (_, _) =>
        {
            // Jendela ujian layar penuh dan Topmost; pastikan dialog yang
            // memegang fokus agar PIN langsung bisa diketik.
            Activate();
            PinBox.Focus();
        };
        Closed += (_, _) => _closed = true;
    }

    private void OnPinKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnConfirm(sender, new RoutedEventArgs());
        else if (e.Key == Key.Escape) OnCancel(sender, new RoutedEventArgs());
    }

    private async void OnConfirm(object? sender, RoutedEventArgs e)
    {
        if (_checking) return;

        var pin = (PinBox.Text ?? "").Trim();
        if (pin.Length == 0)
        {
            ShowStatus("PIN belum diisi.", isError: true);
            return;
        }

        _checking = true;
        PinBox.IsEnabled = false;
        ConfirmButton.IsEnabled = false;
        ShowStatus("Memeriksa PIN ke server…", isError: false);

        PinCheck check;
        try
        {
            check = await _verify(pin);
        }
        catch (Exception ex)
        {
            check = PinCheck.Reject($"Pemeriksaan PIN gagal: {ex.Message}");
        }
        finally
        {
            _checking = false;
        }

        if (_closed) return; // pengawas menekan Batal selama pemeriksaan

        if (check.Accepted)
        {
            Close(true);
            return;
        }

        PinBox.IsEnabled = true;
        ConfirmButton.IsEnabled = true;
        PinBox.Text = "";
        ShowStatus(check.Message, isError: true);
        PinBox.Focus();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        if (!_closed) Close(false);
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusLine.Text = message;
        StatusLine.Foreground = SolidColorBrush.Parse(isError ? "#B4232B" : "#6B7A8D");
        StatusLine.IsVisible = message.Length > 0;
    }
}
