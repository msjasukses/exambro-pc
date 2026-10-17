/*
 * Disuntikkan ke setiap halaman ujian sebelum skrip halaman berjalan.
 * Lapis ini menutup jalur menyalin soal lewat DOM; lapis proses (keyboard hook,
 * anti-screenshot) ditangani sisi native.
 *
 * Halaman ujian yang sah tetap berfungsi: hanya seleksi, menu konteks, dan
 * pintasan penyalinan yang dimatikan — input, tombol, dan textarea dibiarkan.
 */
(function () {
    'use strict';

    var report = function (code, detail) {
        try { window.chrome.webview.postMessage({ type: 'violation', code: code, detail: detail || '' }); }
        catch (e) { /* host belum siap; abaikan */ }
    };

    // --- 1. Matikan seleksi teks & drag, kecuali pada field jawaban --------
    var style = document.createElement('style');
    style.textContent =
        '*:not(input):not(textarea):not([contenteditable="true"]) {' +
        '  -webkit-user-select: none !important;' +
        '  -moz-user-select: none !important;' +
        '  user-select: none !important;' +
        '  -webkit-touch-callout: none !important;' +
        '}' +
        'img, svg, canvas { -webkit-user-drag: none !important; pointer-events: auto; }';
    (document.head || document.documentElement).appendChild(style);

    // --- 2. Blokir menu konteks dan clipboard -----------------------------
    var isAnswerField = function (el) {
        if (!el) return false;
        var tag = (el.tagName || '').toLowerCase();
        return tag === 'input' || tag === 'textarea' || el.isContentEditable;
    };

    ['contextmenu', 'selectstart', 'dragstart'].forEach(function (evt) {
        document.addEventListener(evt, function (e) {
            if (isAnswerField(e.target)) return;
            e.preventDefault();
        }, true);
    });

    ['copy', 'cut', 'paste'].forEach(function (evt) {
        document.addEventListener(evt, function (e) {
            e.preventDefault();
            if (e.clipboardData) e.clipboardData.setData('text/plain', '');
            report('CLIPBOARD', evt);
        }, true);
    });

    // --- 3. Blokir pintasan papan ketik tingkat halaman --------------------
    // Ctrl+C/X/V/A/S/P/U/F, F12, Ctrl+Shift+I/J/C (DevTools).
    var blockedWithCtrl = ['c', 'x', 'v', 'a', 's', 'p', 'u', 'f', 'g', 'h', 'o', 'n', 't', 'w'];
    var devtoolsCombo = ['i', 'j', 'c'];

    document.addEventListener('keydown', function (e) {
        var key = (e.key || '').toLowerCase();
        var ctrl = e.ctrlKey || e.metaKey;

        if (key === 'f12') { e.preventDefault(); report('DEVTOOLS', 'F12'); return; }

        if (ctrl && e.shiftKey && devtoolsCombo.indexOf(key) !== -1) {
            e.preventDefault(); report('DEVTOOLS', 'Ctrl+Shift+' + key.toUpperCase()); return;
        }

        if (ctrl && blockedWithCtrl.indexOf(key) !== -1) {
            // Ctrl+C/V di dalam kolom jawaban tetap ditolak: itulah jalur
            // menempelkan jawaban dari luar.
            e.preventDefault();
            report('SHORTCUT', 'Ctrl+' + key.toUpperCase());
            return;
        }

        if (key === 'printscreen') { e.preventDefault(); report('PRINTSCREEN', ''); }
    }, true);

    // --- 4. Matikan pencetakan --------------------------------------------
    window.print = function () { report('PRINT', 'window.print()'); };
    var printStyle = document.createElement('style');
    printStyle.media = 'print';
    printStyle.textContent = 'html, body { display: none !important; }';
    (document.head || document.documentElement).appendChild(printStyle);

    // --- 5. Laporkan saat tab disembunyikan --------------------------------
    document.addEventListener('visibilitychange', function () {
        if (document.hidden) report('PAGE_HIDDEN', '');
    });

    // --- 6. Tutup jalur membuka jendela baru di luar white-list ------------
    window.open = function (url) { report('POPUP', String(url || '')); return null; };
})();
