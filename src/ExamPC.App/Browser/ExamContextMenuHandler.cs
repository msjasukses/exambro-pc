using Xilium.CefGlue;
using Xilium.CefGlue.Common.Handlers;

namespace ExamPC.App.Browser;

/// <summary>
/// Mengosongkan menu klik kanan. Menu bawaan CEF memuat "Simpan gambar sebagai",
/// "Lihat sumber halaman", dan "Inspect" — tiga jalan keluar sekaligus.
/// Ini pelengkap blokir <c>contextmenu</c> di hardening.js: yang di JS bisa
/// dilewati halaman yang memanggil <c>stopPropagation</c>, yang di sini tidak.
/// </summary>
internal sealed class ExamContextMenuHandler : ContextMenuHandler
{
    protected override void OnBeforeContextMenu(
        CefBrowser browser, CefFrame frame, CefContextMenuParams state, CefMenuModel model)
        => model.Clear();

    protected override bool RunContextMenu(
        CefBrowser browser, CefFrame frame, CefContextMenuParams parameters,
        CefMenuModel model, CefRunContextMenuCallback callback)
    {
        callback.Cancel();
        return true; // true = kami yang menangani, jangan tampilkan menu apa pun
    }
}
