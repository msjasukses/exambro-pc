using ExamPC.Core.Security;
using Xilium.CefGlue;
using Xilium.CefGlue.Common.Handlers;

namespace ExamPC.App.Browser;

/// <summary>
/// Lapis pertama dan kedua white-list URL, langsung di tingkat CEF.
///
/// <see cref="OnBeforeBrowse"/> menahan navigasi utama — klik tautan,
/// <c>location.href</c>, submit form. <see cref="GetResourceRequestHandler"/>
/// menyerahkan setiap resource (gambar, skrip, XHR, iframe) ke
/// <see cref="ExamResourceRequestHandler"/>. Dua-duanya perlu: menahan navigasi
/// saja tidak menutup halaman ujian yang disusupi iframe dari domain lain.
/// </summary>
internal sealed class ExamRequestHandler(
    UrlWhitelist whitelist,
    Action<string, string> record) : RequestHandler
{
    private readonly ExamResourceRequestHandler _resourceHandler = new(whitelist, record);

    protected override bool OnBeforeBrowse(
        CefBrowser browser, CefFrame frame, CefRequest request, bool userGesture, bool isRedirect)
    {
        if (whitelist.IsAllowed(request.Url)) return false; // false = lanjutkan

        record("NAV_BLOCKED", request.Url);
        return true; // true = batalkan navigasi
    }

    protected override CefResourceRequestHandler? GetResourceRequestHandler(
        CefBrowser browser, CefFrame frame, CefRequest request,
        bool isNavigation, bool isDownload, string requestInitiator,
        ref bool disableDefaultHandling)
    {
        // Unduhan tidak pernah diizinkan: berkas yang tersimpan ke disk bisa
        // dibuka kembali setelah sesi berakhir.
        if (isDownload)
        {
            record("DOWNLOAD_BLOCKED", request.Url);
            return null;
        }

        return _resourceHandler;
    }

    /// <summary>
    /// Tautan yang meminta dibuka di tab baru. Di ujian tidak ada tab lain,
    /// jadi permintaannya ditolak dan dicatat.
    /// </summary>
    protected override bool OnOpenUrlFromTab(
        CefBrowser browser, CefFrame frame, string targetUrl,
        CefWindowOpenDisposition targetDisposition, bool userGesture)
    {
        record("NEW_TAB_BLOCKED", targetUrl);
        return true; // true = batalkan
    }

    /// <summary>
    /// Sertifikat HTTPS bermasalah tidak boleh bisa di-klik-lanjutkan oleh siswa —
    /// itu jalur klasik man-in-the-middle di jaringan lab.
    /// </summary>
    protected override bool OnCertificateError(
        CefBrowser browser, CefErrorCode certError, string requestUrl,
        CefSslInfo sslInfo, CefCallback callback)
    {
        record("CERT_ERROR", $"{certError} pada {requestUrl}");
        callback.Dispose();
        return false; // false = tolak, jangan tampilkan pilihan "lanjutkan"
    }

    /// <summary>Proses render CEF mati — halaman ujian hilang dan harus dimuat ulang.</summary>
    protected override void OnRenderProcessTerminated(CefBrowser browser, CefTerminationStatus status)
        => record("RENDERER_CRASHED", status.ToString());
}
