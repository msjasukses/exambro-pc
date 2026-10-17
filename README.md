# ExamPC — Peramban Ujian untuk PC & Laptop

Versi desktop dari [`exbro`](../exbro) / [`exambro`](../exambro). Pengawas memasukkan URL ujian,
aplikasi mengunci perangkat pada halaman itu, dan sesi hanya bisa diakhiri dengan PIN pengawas.

- .NET 8 · Avalonia UI 11 · Chromium Embedded Framework
- Target: **Windows 10/11**, **macOS**, **Linux** (X11 & Wayland)

> **Status: terverifikasi berjalan.** `dotnet build` bersih (0 error), aplikasi terbuka,
> sesi ujian merender halaman lewat CEF, dan installer `distxamPC-Setup-1.0.0.exe`
> (144 MB) berhasil dibuat. Lihat [§7 Status pengujian](#7-status-pengujian) untuk apa
> yang sudah dan belum diuji.

---

## 1. Kekuatan proteksi tidak sama di setiap OS

Ini bagian terpenting yang harus dipahami sebelum memilih perangkat ujian. Windows memberi
aplikasi biasa jauh lebih banyak kewenangan; macOS dan Linux modern sengaja menutupnya —
justru demi keamanan pengguna, dan itu ikut mengunci kita.

| Proteksi | Windows 10/11 | macOS | Linux |
|---|:--:|:--:|:--:|
| Sembunyikan jendela dari screenshot & screen-share | **Ya** | Tidak | Tidak |
| Kunci Alt+Tab / Super / Alt+F4 / PrintScreen | **Ya** | Tidak | Tidak |
| Sembunyikan taskbar & tarik paksa fokus | **Ya** | Tidak | Tidak |
| White-list URL (navigasi + setiap resource) | **Ya** | **Ya** | **Ya** |
| Matikan salin-tempel, klik kanan, cetak, DevTools | **Ya** | **Ya** | **Ya** |
| Deteksi remote desktop | **Ya** | **Ya** | **Ya** |
| Deteksi layar ganda | **Ya** | **Ya** | X11 saja |
| Deteksi mesin virtual | **Ya** | **Ya** | **Ya** |
| Deteksi aplikasi terlarang berjalan | **Ya** | **Ya** | **Ya** |
| Catat & bekukan saat kehilangan fokus | **Ya** | **Ya** | **Ya** |

**Kesimpulan praktis:** untuk ujian bertaruhan tinggi, pakai lab Windows. Di macOS dan Linux,
ExamPC berperan sebagai *pencatat dan pembeku* — setiap pelanggaran tercatat dan sesi terkunci,
tetapi tidak bisa mencegah screenshot. Pengawasan langsung tetap wajib di sana.

Yang **tidak bisa** dilakukan aplikasi mana pun tanpa hak administrator, di OS mana pun:
memblokir **Ctrl+Alt+Del**. Itu Secure Attention Sequence milik kernel Windows. ExamPC
menanganinya dengan cara lain: begitu jendela ujian kehilangan fokus, sesi dibekukan dan
kejadiannya tercatat lengkap dengan waktu. Safe Exam Browser pun memakai pendekatan yang sama.

---

## 2. Cara membangun

Butuh [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0):

```bash
winget install Microsoft.DotNet.SDK.8
```

Lalu:

```bash
dotnet restore
```

```bash
dotnet build -c Release
```

CEF membawa binari native per-platform, jadi build yang dipakai di lab harus RID-spesifik:

```bash
dotnet publish src/ExamPC.App -c Release -r win-x64 --self-contained -o publish/windows
```

```bash
dotnet publish src/ExamPC.App -c Release -r osx-arm64 --self-contained -o publish/macos
```

```bash
dotnet publish src/ExamPC.App -c Release -r linux-x64 --self-contained -o publish/linux
```

Hasilnya ± 120–160 MB per platform, sebagian besar dari CEF. Ini harga yang dibayar untuk
satu mesin render yang sama di semua OS **dan** kemampuan mencegat setiap resource request —
hal yang tidak bisa dilakukan WebView bawaan sistem.

---

## 3. Alur pemakaian

1. **Layar penyiapan** — pengawas mengisi URL ujian, domain tambahan yang diizinkan, label
   ruang, PIN keluar, dan memilih proteksi yang aktif.
2. **Periksa Perangkat** — pemindaian pra-ujian. Temuan dikelompokkan jadi tiga:
   - **BLOKIR** — ujian tidak boleh dimulai (remote desktop, layar ganda, VM, perekam layar).
   - **PERINGATAN** — boleh lanjut, tetapi pengawas perlu tahu (mis. proteksi yang tidak
     tersedia di OS ini, atau aplikasi perpesanan yang terbuka).
   - **AMAN** — tidak ada temuan.

   Tombol **Mulai Ujian** menolak berjalan selama masih ada temuan BLOKIR, dan menolak
   berjalan sama sekali bila pemindaian belum pernah dijalankan.
3. **Sesi ujian** — layar penuh, tanpa dekorasi jendela, selalu di depan.
4. **Keluar** — ketuk bilah kepala **5 kali dalam 3 detik**, lalu masukkan PIN pengawas.
   Tidak ada tombol keluar yang terlihat; Alt+F4 dan perintah tutup lainnya ditolak.
5. **Ringkasan sesi** — seluruh pelanggaran ditampilkan sekaligus, plus lokasi berkas log.

---

## 4. Peta implementasi

| Berkas | Isi |
|---|---|
| [`ExamConfig.cs`](src/ExamPC.Core/Config/ExamConfig.cs) | Seluruh sakelar proteksi dan pengaturan sesi |
| [`ConfigStore.cs`](src/ExamPC.Core/Config/ConfigStore.cs) | Baca/tulis konfigurasi ke `%APPDATA%\ExamPC` |
| [`UrlWhitelist.cs`](src/ExamPC.Core/Security/UrlWhitelist.cs) | Penentu URL; wildcard `*.domain`, tolak skema non-http |
| [`PinHasher.cs`](src/ExamPC.Core/Security/PinHasher.cs) | PBKDF2-SHA256, 210.000 iterasi |
| [`ProcessBlacklist.cs`](src/ExamPC.Core/Security/ProcessBlacklist.cs) | 40+ proses terlarang, dikelompokkan per kategori |
| [`PreflightScanner.cs`](src/ExamPC.Core/Security/PreflightScanner.cs) | Pemindaian pra-ujian, penentu BLOKIR/PERINGATAN |
| [`ViolationLog.cs`](src/ExamPC.Core/Security/ViolationLog.cs) | Log JSONL lokal + POST webhook (usaha terbaik) |
| [`hardening.js`](src/ExamPC.Core/Assets/hardening.js) | Pengerasan di dalam halaman: seleksi, clipboard, pintasan, cetak |
| [`IPlatformSecurity.cs`](src/ExamPC.Core/Security/IPlatformSecurity.cs) | Batas antara logika lintas platform dan kode per-OS |
| [`WindowsSecurity.cs`](src/ExamPC.Platform/Windows/WindowsSecurity.cs) | `WDA_EXCLUDEFROMCAPTURE`, `WH_KEYBOARD_LL`, WMI, taskbar |
| [`MacOsSecurity.cs`](src/ExamPC.Platform/MacOS/MacOsSecurity.cs) | `system_profiler`, `sysctl`, `pbcopy` |
| [`LinuxSecurity.cs`](src/ExamPC.Platform/Linux/LinuxSecurity.cs) | `xrandr`, `systemd-detect-virt`, DMI |
| [`ExamWindow.axaml.cs`](src/ExamPC.App/Views/ExamWindow.axaml.cs) | Sesi ujian: watchdog, pembekuan, jalan keluar |

### Lapisan white-list URL

Tiga lapis, karena satu lapis saja bisa dilewati:

1. [`ExamRequestHandler.OnBeforeBrowse`](src/ExamPC.App/Browser/ExamRequestHandler.cs) — menahan
   klik tautan, `location.href`, dan submit form.
2. [`ExamResourceRequestHandler`](src/ExamPC.App/Browser/ExamResourceRequestHandler.cs) — menahan
   **setiap** gambar, skrip, XHR, dan iframe, termasuk memeriksa ulang tujuan pengalihan.
   Ini yang menutup celah halaman ujian yang disusupi iframe dari domain lain.
3. [`ExamLifeSpanHandler`](src/ExamPC.App/Browser/ExamLifeSpanHandler.cs) dan
   [`hardening.js`](src/ExamPC.Core/Assets/hardening.js) — menutup pop-up dan `window.open`.

### Watchdog tiga detik

Pemindaian pra-ujian hanya memotret keadaan di detik nol. Siswa yang menjalankan AnyDesk atau
mencolok monitor kedua **setelah** ujian dimulai akan tertangkap oleh watchdog, yang langsung
membekukan sesi.

---

## 5. Pengerasan yang wajib dilakukan di sisi lab

Kode aplikasi tidak bisa menggantikan penyiapan perangkat. Untuk ujian bertaruhan tinggi di
lab Windows, lakukan juga:

1. **Assigned Access / Shell Launcher** (Windows Pro/Education/Enterprise) — jadikan
   `ExamPC.exe` sebagai shell pengganti `explorer.exe`. Ini satu-satunya cara resmi mematikan
   Ctrl+Alt+Del dan Task Manager.
2. **Group Policy** — matikan Task Manager, Registry Editor, dan Command Prompt untuk akun siswa.
3. **Akun siswa non-administrator** — tanpa hak admin, siswa tidak bisa memasang AnyDesk di tengah ujian.
4. **Firewall keluar** — izinkan hanya domain server ujian. White-list di aplikasi menutup jalur
   dari dalam peramban; firewall menutup jalur dari luar aplikasi.
5. **Nonaktifkan port USB** bila memungkinkan.

---

## 6. Integrasi dengan server CBT

Log pelanggaran dikirim sebagai POST JSON ke `ViolationWebhookUrl`:

```json
{
  "At": "2026-09-10T08:14:22.113+07:00",
  "Code": "FOCUS_LOST",
  "Detail": "kejadian ke-2",
  "Machine": "LAB1-PC07",
  "Room": "Lab 1 — Sesi Pagi"
}
```

Kode yang mungkin muncul: `FOCUS_LOST`, `HOTKEY`, `KEY`, `CLIPBOARD`, `NAV_BLOCKED`,
`RESOURCE_BLOCKED`, `DEVTOOLS`, `PRINTSCREEN`, `POPUP`, `FROZEN`, `UNFROZEN`, `PIN_WRONG`,
`CLOSE_ATTEMPT`, `PAGE_HIDDEN`.

Di sisi Laravel ([`cbt`](../cbt) / [`ujian`](../ujian)), cukup satu rute POST yang menyimpan
payload ini dan menandai peserta yang pelanggarannya menumpuk. Log tetap ditulis ke
`%APPDATA%\ExamPC\logs\pelanggaran-<waktu>.jsonl` walau jaringan lab putus — berkas lokal
adalah sumber kebenaran, webhook hanya usaha terbaik.

---

## 7. Status pengujian

### Sudah diuji dan berjalan

| Yang diuji | Hasil |
|---|---|
| `dotnet build -c Debug` | 0 error, 0 warning |
| `dotnet publish -c Release -r win-x64 --self-contained` | 453 MB folder publish |
| Aplikasi dibuka (build Debug maupun Release) | Layar penyiapan tampil normal |
| Sesi ujian dengan CEF | Halaman ter-render, layar penuh, bilah kepala + timer jalan |
| Kompilasi installer | `dist\ExamPC-Setup-1.0.0.exe`, 144 MB |

### Belum diuji — perlu perangkat dan orang sungguhan

1. **Proteksi Windows di kondisi nyata.** Pengujian di atas sengaja menjalankan sesi
   dengan seluruh proteksi **dimatikan**, supaya prosesnya aman dihentikan. Anti-screenshot,
   hook tombol sistem, penyembunyian taskbar, dan pembekuan saat kehilangan fokus belum
   pernah diaktifkan sekali pun. Uji satu per satu, di perangkat fisik, bukan VM.
2. **Pemasangan lewat installer.** Kompilasinya terbukti, tetapi `ExamPC-Setup-1.0.0.exe`
   belum pernah benar-benar dijalankan — instalasi butuh hak administrator.
3. **Halaman ujian sungguhan.** Yang diuji baru `https://example.com`. Halaman CBT Laravel
   Anda memuat skrip, gambar, dan XHR dari beberapa asal; pastikan semuanya masuk daftar
   putih, atau soal akan tampil tanpa gaya dan tanpa gambar.
4. **macOS dan Linux.** Hanya Windows yang pernah dibangun dan dijalankan.

### Windows Defender menandai kode hook papan ketik

Saat membangun, Defender pernah mengunci
[`NativeMethods.cs`](src/ExamPC.Platform/Windows/NativeMethods.cs) sebagai *"virus or
potentially unwanted software"* dan menggagalkan satu build. Percobaan berikutnya lolos,
jadi sifatnya tidak konsisten — tetapi penyebabnya nyata: deklarasi `SetWindowsHookEx` +
`WH_KEYBOARD_LL` memang sidik jari yang sama dengan keylogger.

Artinya `ExamPC.exe` **berpotensi diblokir antivirus di PC lab**. Tiga jalan keluar yang sah:

1. **Exclusion lewat Group Policy** untuk `C:\Program Files\ExamPC` — paling praktis untuk lab.
2. **Sertifikat code signing** (lihat §9).
3. **Lapor false-positive** ke Microsoft lewat portal Windows Defender Security Intelligence.

Ada juga jalan keempat yang menghilangkan masalahnya dari akar: **buang hook papan ketik
sama sekali** dan kunci perangkat lewat **Assigned Access / Shell Launcher** bawaan Windows
(§5). Untuk lab sekolah pendekatan itu justru lebih kuat — ia mematikan Ctrl+Alt+Del dan Task
Manager, yang tidak bisa dilakukan hook mana pun — sekaligus tidak menyentuh antivirus.

---

## 8. Membuat installer Windows (`.exe`)

Satu perintah, menghasilkan `dist\ExamPC-Setup-1.0.3.exe` yang dipasang seperti aplikasi biasa
— masuk daftar Program & Fitur, punya pintasan Start Menu, dan bisa di-uninstall.

### Sekali saja: pasang perkakas

```bash
winget install Microsoft.DotNet.SDK.8
```

```bash
winget install JRSoftware.InnoSetup
```

Tutup dan buka ulang terminal setelahnya agar PATH terbaca.

### Setiap kali merilis

```powershell
.\scripts\build-installer.ps1 -Version 1.0.3
```

[Skripnya](scripts/build-installer.ps1) menjalankan tiga tahap dan berhenti dengan pesan jelas
bila salah satu gagal:

1. **Publish** — `dotnet publish -r win-x64 --self-contained`. Folder publish lama dihapus dulu,
   supaya DLL dari build sebelumnya tidak ikut terkemas.
2. **Verifikasi** — memastikan `ExamPC.exe`, `libcef.dll`, dan `icudtl.dat` betul-betul ada.
   Tanpa pemeriksaan ini, installer bisa jadi dan terpasang mulus, lalu peramban ujian gagal
   terbuka saat siswa sudah duduk di depan PC.
3. **Inno Setup** — mengemas seluruh folder jadi satu installer.

Sedang menyetel skrip installer dan tidak ingin menunggu build ulang:

```powershell
.\scripts\build-installer.ps1 -Version 1.0.3 -SkipPublish
```

### Memasang di PC lab

Satu PC — klik dua kali installer-nya. Instalasi butuh hak administrator karena menulis ke
Program Files; **aplikasinya sendiri berjalan tanpa hak admin**, jadi siswa tidak pernah melihat
dialog UAC saat ujian dimulai.

Puluhan PC sekaligus — mode senyap, tanpa satu pun dialog:

```bash
ExamPC-Setup-1.0.3.exe /VERYSILENT /NORESTART /SUPPRESSMSGBOXES
```

Cocok dijalankan lewat Group Policy startup script, PDQ Deploy, atau PsExec.

### Yang dilakukan installer

| | |
|---|---|
| Lokasi | `C:\Program Files\ExamPC` |
| Pintasan | Start Menu, dan desktop bila dicentang |
| Aturan firewall | Opsional, dicentang saat pasang — izin keluar untuk `ExamPC.exe` |
| Perlindungan saat ujian | **Menolak dipasang atau di-uninstall selama sesi ujian berjalan** |
| Log pelanggaran | **Tidak dihapus saat uninstall** |

Dua baris terakhir itu sengaja. Tanpa yang pertama, siswa berhak admin bisa mengakhiri ujiannya
sendiri lewat installer. Dan uninstall tidak boleh menghapus `%APPDATA%\ExamPC\logs` — di situ
satu-satunya bukti bila ada sengketa nilai. Hapus manual bila memang perlu.

Sebelum merilis versi berikutnya, jangan ubah `AppId` di [`ExamPC.iss`](installer/ExamPC.iss).
GUID itu yang dipakai Windows untuk mengenali dan menimpa versi lama; GUID baru berarti dua
ExamPC terpasang berdampingan di PC lab.

---

## 9. SmartScreen dan tanda tangan digital

Installer hasil skrip di atas **belum ditandatangani**. Pada PC yang baru pertama kali
menjalankannya, Windows akan menampilkan:

> Windows protected your PC — *More info* → *Run anyway*

Ini normal untuk aplikasi tanpa sertifikat, bukan tanda ada yang salah. Tiga pilihan:

1. **Biarkan** — teknisi lab menekan *Run anyway* sekali per PC. Paling murah, dan untuk
   instalasi internal sekolah biasanya cukup.
2. **Pasang lewat mode senyap** — `/VERYSILENT` yang dijalankan dari Group Policy atau PsExec
   sebagai SYSTEM tidak melewati SmartScreen sama sekali. Ini cara paling praktis untuk lab.
3. **Beli sertifikat code signing** — OV sekitar $200–400/tahun, EV (langsung dipercaya
   SmartScreen) $300–600/tahun. Setelah punya sertifikatnya, tambahkan `signtool` ke
   `[Setup]` di `ExamPC.iss`:

   ```
   SignTool=signtool sign /fd sha256 /tr http://timestamp.digicert.com /td sha256 $f
   ```

Hanya tempuh opsi 3 bila ExamPC akan disebar ke luar sekolah Anda sendiri.
