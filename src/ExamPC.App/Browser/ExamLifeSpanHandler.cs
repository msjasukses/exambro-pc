using Xilium.CefGlue;
using Xilium.CefGlue.Common.Handlers;

namespace ExamPC.App.Browser;

/// <summary>
/// Menolak setiap jendela pop-up. Pop-up adalah jendela CEF baru di luar
/// jendela ujian yang sudah dilindungi anti-screenshot — sekali terbuka,
/// proteksinya tidak berlaku di sana.
/// </summary>
internal sealed class ExamLifeSpanHandler(Action<string, string> record) : LifeSpanHandler
{
    protected override bool OnBeforePopup(
        CefBrowser browser, CefFrame frame, string targetUrl, string targetFrameName,
        CefWindowOpenDisposition targetDisposition, bool userGesture,
        CefPopupFeatures popupFeatures, CefWindowInfo windowInfo,
        ref CefClient client, CefBrowserSettings settings,
        ref CefDictionaryValue extraInfo, ref bool noJavascriptAccess)
    {
        record("POPUP_BLOCKED", targetUrl);
        return true; // true = batalkan pembuatan pop-up
    }
}
