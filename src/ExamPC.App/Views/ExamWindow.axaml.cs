using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ExamPC.Core.Config;
using ExamPC.Core.Security;
using ExamPC.App.Browser;
using ExamPC.Platform;
using ExamPC.Platform.Windows;
using Xilium.CefGlue;
using Xilium.CefGlue.Avalonia;

namespace ExamPC.App.Views;

public partial class ExamWindow : Window
{
    private readonly ExamConfig _config;
    private readonly IPlatformSecurity _platform;
    private readonly UrlWhitelist _whitelist;
    private readonly ViolationLog _log;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;

    /// <summary>ID instalasi untuk pembatasan PIN salah per PC di server — sama seperti installId exbro.</summary>
    private readonly string _deviceId = ExitPinVerifier.GetInstallId(ConfigStore.DataDirectory);

    private readonly DispatcherTimer _watchdog = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>
    /// Dibuat di sini, bukan di XAML: konstruktor AvaloniaCefBrowser punya
    /// parameter opsional, dan compiler XAML Avalonia hanya menerima konstruktor
    /// tanpa parameter sama sekali.
    /// </summary>
    private readonly AvaloniaCefBrowser Browser = new();

    private int _focusLossCount;
    private bool _frozen;
    private bool _closingApproved;

    /// <summary>
    /// Dialog PIN sedang terbuka. Selama itu jendela ujian memang tidak aktif,
    /// dan itu bukan pelanggaran: tanpa penanda ini setiap kali dialog muncul
    /// dihitung "kehilangan fokus", watchdog merebut fokus dari dialog, dan
    /// setelah beberapa kali sesi diam-diam beku sehingga ketukan pada bilah
    /// kepala tidak lagi memunculkan dialog.
    /// </summary>
    private bool _pinDialogOpen;

    /// <summary>Proses logout sedang berjalan; tombol dan watchdog tidak boleh ikut campur.</summary>
    private bool _exiting;

    // Ketukan rahasia pada bilah kepala untuk memanggil dialog PIN.
    private int _headerTaps;
    private DateTime _firstTapAt;
    private const int TapsToExit = 5;
    private static readonly TimeSpan TapWindow = TimeSpan.FromSeconds(3);

    /// <summary>Jeda menunggu server memproses halaman logout sebelum cookie dihapus.</summary>
    private static readonly TimeSpan LogoutWait = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// Mencari jalan logout aplikasi web di halaman aktif. Form diutamakan karena
    /// logout Laravel wajib POST dengan token CSRF — membuka tautannya lewat GET
    /// hanya menghasilkan 405. Mengembalikan "form" bila form sudah dikirim,
    /// URL tautan logout, atau string kosong bila tidak ditemukan.
    /// </summary>
    private const string LogoutScript = """
        (function () {
          try {
            var hit = function (s) {
              s = (s || '').toLowerCase();
              return s.indexOf('logout') >= 0 || s.indexOf('signout') >= 0 ||
                     s.indexOf('sign-out') >= 0 || s.indexOf('keluar') >= 0;
            };
            var forms = document.querySelectorAll('form[action]');
            for (var i = 0; i < forms.length; i++) {
              if (hit(forms[i].getAttribute('action'))) {
                HTMLFormElement.prototype.submit.call(forms[i]);
                return 'form';
              }
            }
            var links = document.querySelectorAll('a[href]');
            for (var j = 0; j < links.length; j++) {
              var href = links[j].getAttribute('href') || '';
              if (href.charAt(0) === '#' || href.toLowerCase().indexOf('javascript:') === 0) continue;
              if (hit(href) || hit(links[j].textContent)) return links[j].href;
            }
          } catch (e) {}
          return '';
        })();
        """;

    // Hanya dipakai perancang XAML; jalur normal selalu lewat SetupWindow.
    public ExamWindow() : this(new ExamConfig(), PlatformSecurityFactory.Create()) { }

    public ExamWindow(ExamConfig config, IPlatformSecurity platform)
    {
        _config = config;
        _platform = platform;
        _whitelist = new UrlWhitelist(config.ExamUrl, config.ExtraAllowedHosts);
        _log = new ViolationLog(ConfigStore.LogDirectory, config.ViolationWebhookUrl, config.RoomLabel);

        InitializeComponent();
        BrowserHost.Children.Add(Browser);

        RoomLine.Text = config.RoomLabel.Trim();
        RoomChip.IsVisible = RoomLine.Text.Length > 0;
        UpdateViolationLine();

        ConfigureBrowser();

        Opened += OnOpened;
        Closing += OnClosing;
        Deactivated += OnDeactivated;
        KeyDown += OnKeyDown;

        _watchdog.Tick += OnWatchdogTick;
        _clock.Tick += (_, _) => ClockLine.Text = (DateTimeOffset.Now - _startedAt).ToString(@"hh\:mm\:ss");
    }

    // ---- Peramban ----------------------------------------------------------

    private void ConfigureBrowser()
    {
        // Lapis 1 & 2 — white-list URL di tingkat CEF. ExamRequestHandler menahan
        // navigasi utama, dan menyerahkan setiap resource ke handler turunannya.
        Browser.RequestHandler = new ExamRequestHandler(_whitelist, Record);

        // Pop-up adalah jendela CEF baru di luar jendela yang sudah dilindungi
        // anti-screenshot, jadi tidak boleh pernah terbuka.
        Browser.LifeSpanHandler = new ExamLifeSpanHandler(Record);

        // Menu klik kanan bawaan CEF memuat "Simpan gambar", "Lihat sumber",
        // dan "Inspect" — tiga jalan keluar sekaligus.
        Browser.ContextMenuHandler = new ExamContextMenuHandler();

        // Lapis 3 — pengerasan di dalam halaman. Dipasang pada LoadEnd, bukan
        // LoadStart: pada LoadStart DOM belum ada, sehingga penyuntikan CSS-nya
        // tidak menempel. Dipasang ulang setiap dokumen karena setiap navigasi
        // memberi DOM yang bersih.
        if (_config.BlockClipboard)
        {
            Browser.LoadEnd += (_, e) =>
            {
                if (!e.Frame.IsMain) return; // iframe tidak perlu, dan sering lintas-origin

                try { Browser.ExecuteJavaScript(HardeningScript.Source, "exampc://hardening.js", 1); }
                catch (Exception ex) { Record("INJECT_FAILED", ex.Message); }
            };
        }

        // Garis tipis di bawah bilah kepala selama halaman dimuat. Event datang dari
        // thread CEF, jadi perubahan tampilan dilempar ke thread UI.
        Browser.LoadingStateChange += (_, e) =>
            Dispatcher.UIThread.Post(() => LoadingBar.IsVisible = e.IsLoading);

        Browser.LoadError += (_, e) =>
        {
            // Halaman ujian gagal dimuat adalah masalah pengawas, bukan siswa —
            // catat agar terlihat di ringkasan sesi.
            if (e.ErrorCode != CefErrorCode.Aborted)
                Record("LOAD_ERROR", $"{e.ErrorCode}: {e.FailedUrl}");
        };

        // Galat asinkron dari CEF dicatat saja. CefGlue tidak menyediakan cara
        // menandainya sudah ditangani, tetapi galat ini tidak mematikan proses —
        // yang penting jejaknya masuk ke log pelanggaran.
        Browser.UnhandledException += (_, e) => Record("BROWSER_ERROR", e.Exception.Message);
    }

    // ---- Siklus hidup sesi -------------------------------------------------

    private void OnOpened(object? sender, EventArgs e)
    {
        ApplyProtections();

        Browser.Address = _config.ExamUrl;

        _watchdog.Start();
        _clock.Start();
        Activate();
    }

    private void ApplyProtections()
    {
        var handle = TryGetNativeHandle();

        if (_config.BlockScreenCapture)
        {
            var result = _platform.TryBlockScreenCapture(handle);
            if (!result.Applied) Record("CAPTURE_BLOCK_FAILED", result.Note);
        }

        if (_config.BlockSystemHotkeys)
        {
            var result = _platform.TryBlockSystemHotkeys();
            if (!result.Applied) Record("HOTKEY_BLOCK_FAILED", result.Note);
        }

        if (OperatingSystem.IsWindows() && _platform is WindowsSecurity win)
        {
            win.HotkeyBlocked += name => Record("HOTKEY", name);
            win.HideTaskbar();
        }
    }

    private IntPtr TryGetNativeHandle()
    {
        try { return TryGetPlatformHandle()?.Handle ?? IntPtr.Zero; }
        catch (Exception) { return IntPtr.Zero; }
    }

    /// <summary>
    /// Denyut tiga detik: memastikan proteksi masih berdiri dan lingkungan
    /// belum berubah di tengah ujian. Siswa yang mencolok monitor kedua atau
    /// menjalankan AnyDesk setelah ujian dimulai tetap tertangkap di sini.
    /// </summary>
    private void OnWatchdogTick(object? sender, EventArgs e)
    {
        if (_frozen || _exiting) return;

        if (_config.RequireSingleDisplay && _platform.GetActiveDisplayCount() > 1)
            Freeze("Layar tambahan terdeteksi selama ujian berlangsung.");

        if (_platform.IsRemoteSession())
            Freeze("Sesi remote desktop terdeteksi selama ujian berlangsung.");

        if (_config.DetectForbiddenProcesses)
        {
            var matches = ProcessBlacklist.Match(_platform.GetRunningProcessNames());
            var blockers = matches
                .Where(m => m.Category is "Remote desktop" or "Perekam layar" or "Otomasi")
                .Select(m => m.DisplayName)
                .Distinct()
                .ToList();

            if (blockers.Count > 0)
                Freeze($"Aplikasi terlarang dijalankan: {string.Join(", ", blockers)}.");
        }

        if (_config.BlockClipboard) _platform.ClearClipboard();

        // Rebut kembali fokus bila ada yang menyelinap ke depan — kecuali dialog
        // PIN kita sendiri, yang justru harus tetap memegang fokus.
        if (OperatingSystem.IsWindows() && _platform is WindowsSecurity win && !_frozen && !_pinDialogOpen)
            win.ForceForeground(TryGetNativeHandle());
    }

    /// <summary>Jendela ujian kehilangan fokus — siswa berpindah ke aplikasi lain.</summary>
    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_frozen || _closingApproved || _pinDialogOpen || _exiting || !_config.LockOnFocusLoss) return;

        _focusLossCount++;
        Record("FOCUS_LOST", $"kejadian ke-{_focusLossCount}");
        UpdateViolationLine();

        if (_focusLossCount > _config.MaxFocusLossBeforeFreeze)
        {
            Freeze($"Jendela ujian ditinggalkan {_focusLossCount} kali.");
        }
        else
        {
            // Masih dalam toleransi: tarik kembali ke depan tanpa membekukan.
            Dispatcher.UIThread.Post(Activate, DispatcherPriority.Background);
        }
    }

    /// <summary>Tombol yang tidak tertangkap hook OS masih bisa disaring di sini.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
                   e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        var blocked =
            e.Key is Key.F11 or Key.F12 or Key.PrintScreen ||
            (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key is Key.F4 or Key.Tab) ||
            (ctrl && e.Key is Key.P or Key.S or Key.U or Key.N or Key.T or Key.W or Key.R) ||
            (ctrl && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key is Key.I or Key.J or Key.C);

        if (!blocked) return;

        e.Handled = true;
        Record("KEY", $"{e.KeyModifiers}+{e.Key}");
    }

    // ---- Pembekuan ---------------------------------------------------------

    private void Freeze(string reason)
    {
        if (_frozen) return;

        _frozen = true;
        FreezeReason.Text = reason;
        FreezeTimeLine.Text = $"Dibekukan pukul {DateTime.Now:HH:mm:ss}";
        FreezeOverlay.IsVisible = true;

        // Peramban disembunyikan, bukan sekadar ditutup selimut: bila CEF berjalan
        // sebagai jendela native, ia selalu tergambar di atas kontrol Avalonia dan
        // selimut pembekuan tidak akan pernah terlihat — sesi beku tanpa tanda apa pun.
        Browser.IsVisible = false;

        Record("FROZEN", reason);
    }

    private async void OnUnfreezeClick(object? sender, RoutedEventArgs e)
    {
        if (!await AskPinAsync("Lanjutkan Sesi")) return;

        _frozen = false;
        FreezeOverlay.IsVisible = false;
        Browser.IsVisible = true;
        Record("UNFROZEN", "dilanjutkan pengawas");
        Activate();
    }

    // ---- Tombol bilah kepala -----------------------------------------------

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (_frozen || _exiting || _pinDialogOpen) return;

        try { Browser.Reload(); }
        catch (Exception ex) { Record("RELOAD_FAILED", ex.Message); }
    }

    private async void OnLogoutClick(object? sender, RoutedEventArgs e)
    {
        if (_exiting) return;
        if (await AskPinAsync("Keluar dari Ujian")) await LogoutAndEndAsync();
    }

    // ---- Jalan keluar ------------------------------------------------------

    private async void OnHeaderTapped(object? sender, PointerPressedEventArgs e)
    {
        if (_pinDialogOpen || _exiting) return;

        var now = DateTime.UtcNow;
        if (now - _firstTapAt > TapWindow)
        {
            _headerTaps = 0;
            _firstTapAt = now;
        }

        if (++_headerTaps < TapsToExit) return;

        ResetTaps();

        // Lepas tangkapan pointer sebelum dialog modal muncul: tombol mouse
        // dilepas di atas jendela dialog, sehingga bilah kepala tidak pernah
        // menerima PointerReleased-nya.
        e.Pointer.Capture(null);
        e.Handled = true;

        if (await AskPinAsync("Akhiri Sesi Ujian")) await LogoutAndEndAsync();
    }

    private void ResetTaps()
    {
        _headerTaps = 0;
        _firstTapAt = DateTime.MinValue;
    }

    private async Task<bool> AskPinAsync(string title)
    {
        if (_pinDialogOpen) return false; // cegah dialog bertumpuk dari klik ganda

        _pinDialogOpen = true;
        try
        {
            return await new PinDialog(title, pin => CheckPinAsync(pin, title)).ShowDialog<bool>(this);
        }
        finally
        {
            _pinDialogOpen = false;
            ResetTaps();
            Activate(); // kembalikan fokus ke jendela ujian, baik dibatalkan maupun tidak
        }
    }

    /// <summary>
    /// Alur yang sama dengan promptExitPin milik exbro: PIN diperiksa ke server CBT,
    /// dan hanya bila server tidak memberi jawaban bertanda tangan sah, PIN cadangan
    /// dari build dipakai agar pengawas tidak terkunci saat server mati.
    /// </summary>
    private async Task<PinCheck> CheckPinAsync(string pin, string purpose)
    {
        var result = await ExitPinVerifier.VerifyAsync(_config.ExamUrl, pin, _deviceId);

        switch (result.Outcome)
        {
            case ExitPinOutcome.Valid:
                return PinCheck.Accept;

            case ExitPinOutcome.Invalid:
                Record("PIN_WRONG", purpose);
                return PinCheck.Reject("PIN salah.");
        }

        if (ExitPinVerifier.MatchesFallbackPin(pin)) return PinCheck.Accept;

        Record("PIN_WRONG", $"{purpose} (PIN cadangan, server: {result.Reason})");
        return PinCheck.Reject("Server PIN tidak terjangkau, memakai PIN cadangan. PIN salah.");
    }

    /// <summary>
    /// Keluar ujian ala Exambro: logout dari aplikasi web, hapus cookie sehingga
    /// siswa wajib login ulang, lalu tutup sesi.
    /// </summary>
    private async Task LogoutAndEndAsync()
    {
        if (_exiting) return;

        _exiting = true;
        _watchdog.Stop();
        RefreshButton.IsEnabled = false;
        LogoutButton.IsEnabled = false;

        await LogoutFromExamSiteAsync();
        await ClearCookiesAsync();

        EndSession();
    }

    /// <summary>
    /// Upaya logout di sisi server: mengirim form atau membuka tautan logout pada
    /// halaman aktif. Bila tidak ditemukan atau gagal, lanjut ke pembersihan cookie.
    /// </summary>
    private async Task LogoutFromExamSiteAsync()
    {
        if (_frozen) return; // peramban tersembunyi; jangan navigasi diam-diam

        try
        {
            var evaluate = Browser.EvaluateJavaScript<string>(LogoutScript);
            if (await Task.WhenAny(evaluate, Task.Delay(TimeSpan.FromSeconds(2))) != evaluate) return;

            var result = (await evaluate)?.Trim() ?? "";
            if (result.Length == 0) return;

            if (result != "form")
            {
                if (!_whitelist.IsAllowed(result)) return;
                Browser.Address = result;
            }

            await Task.Delay(LogoutWait);
        }
        catch (Exception ex)
        {
            Record("LOGOUT_FAILED", ex.Message);
        }
    }

    /// <summary>
    /// Cookie CEF hidup selama proses berjalan, bukan per jendela. Tanpa ini sesi
    /// ujian berikutnya dari layar ringkasan langsung masuk sebagai siswa sebelumnya.
    /// </summary>
    private static Task ClearCookiesAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var manager = CefCookieManager.GetGlobal(null);
            if (manager is null || !manager.DeleteCookies(null, null, new CookiesDeleted(done)))
                done.TrySetResult();
        }
        catch (Exception)
        {
            done.TrySetResult();
        }

        return Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(2)));
    }

    private sealed class CookiesDeleted(TaskCompletionSource done) : CefDeleteCookiesCallback
    {
        protected override void OnComplete(int numDeleted) => done.TrySetResult();
    }

    private void EndSession()
    {
        _closingApproved = true;
        Close();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Alt+F4 yang lolos, atau perintah shutdown, tidak boleh mengakhiri ujian.
        if (!_closingApproved)
        {
            e.Cancel = true;
            Record("CLOSE_ATTEMPT", "penutupan tanpa PIN ditolak");
            return;
        }

        _watchdog.Stop();
        _clock.Stop();

        _platform.ReleaseSystemHotkeys();
        _platform.ClearClipboard();

        if (OperatingSystem.IsWindows() && _platform is WindowsSecurity win)
        {
            win.ReleaseScreenCaptureBlock(TryGetNativeHandle());
            win.ShowTaskbar();
        }

        try { Browser.Dispose(); } catch (Exception) { }

        var summary = new SummaryWindow(_log, _startedAt, _config.RoomLabel);
        _log.Dispose();
        summary.Show();
    }

    // ---- Pencatatan --------------------------------------------------------

    private void Record(string code, string detail)
    {
        _ = _log.RecordAsync(code, detail);
        Dispatcher.UIThread.Post(UpdateViolationLine);
    }

    private void UpdateViolationLine()
    {
        ViolationChip.IsVisible = _log.Count > 0;
        ViolationLine.Text = $"{_log.Count} pelanggaran";
    }
}
