using ExamPC.Core.Security;
using Xilium.CefGlue;

namespace ExamPC.App.Browser;

/// <summary>
/// Menyaring setiap permintaan resource. Inilah lapis yang menutup celah paling
/// halus: halaman ujian yang sah memuat iframe, skrip, atau gambar dari domain
/// di luar daftar putih.
/// </summary>
internal sealed class ExamResourceRequestHandler(
    UrlWhitelist whitelist,
    Action<string, string> record) : CefResourceRequestHandler
{
    /// <summary>
    /// Anggota abstrak yang wajib diisi. <c>null</c> berarti CEF memakai
    /// penanganan cookie bawaannya — yang sudah benar untuk kita, karena
    /// penyimpanan cookie sudah dimatikan lewat CefSettings di Program.cs.
    /// </summary>
    protected override CefCookieAccessFilter? GetCookieAccessFilter(
        CefBrowser browser, CefFrame frame, CefRequest request) => null;

    protected override CefReturnValue OnBeforeResourceLoad(
        CefBrowser browser, CefFrame frame, CefRequest request, CefCallback callback)
    {
        if (whitelist.IsAllowed(request.Url))
            return CefReturnValue.Continue;

        record("RESOURCE_BLOCKED", $"{request.ResourceType}: {request.Url}");
        return CefReturnValue.Cancel;
    }

    /// <summary>
    /// Pengalihan bisa memindahkan permintaan yang tadinya sah ke domain lain,
    /// jadi tujuan pengalihan harus diperiksa ulang.
    /// </summary>
    protected override void OnResourceRedirect(
        CefBrowser browser, CefFrame frame, CefRequest request,
        CefResponse response, ref string newUrl)
    {
        if (whitelist.IsAllowed(newUrl)) return;

        record("REDIRECT_BLOCKED", newUrl);

        // CEF tidak menyediakan cara membatalkan dari sini, jadi pengalihan
        // diarahkan ke about:blank — permintaannya mati tanpa memuat apa pun.
        newUrl = "about:blank";
    }
}
