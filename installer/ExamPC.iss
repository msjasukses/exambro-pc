; ============================================================================
;  ExamPC — skrip installer Inno Setup 6
;
;  Jangan dijalankan langsung. Pakai scripts\build-installer.ps1, yang lebih
;  dulu menjalankan dotnet publish lalu memanggil ISCC.exe dengan parameter
;  yang benar.
;
;  Kompilasi manual (bila publish sudah ada):
;    ISCC.exe installer\ExamPC.iss /DAppVersion=1.0.3 /DPublishDir=..\publish\windows
; ============================================================================

#define AppName        "ExamPC"
#define AppPublisher   "SMKN 45"
#define AppExeName     "ExamPC.exe"
#define AppDescription "Peramban Ujian Terkunci"

; Diisi build-installer.ps1. Nilai bawaan agar kompilasi manual tetap jalan.
#ifndef AppVersion
  #define AppVersion "1.0.3"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish\windows"
#endif

[Setup]
; AppId menentukan identitas aplikasi di daftar Program & Fitur Windows.
; JANGAN PERNAH mengubah GUID ini — installer versi berikutnya memakainya untuk
; mengenali dan menimpa versi lama. GUID baru berarti dua ExamPC terpasang
; berdampingan di PC lab, dan itu mimpi buruk saat hari ujian.
AppId={{9F8271F3-E32E-4896-A20A-EAC61589ABC4}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} — {#AppDescription}

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayName={#AppName} {#AppVersion}
UninstallDisplayIcon={app}\{#AppExeName}

; Instalasi menulis ke Program Files, jadi butuh hak administrator. Aplikasinya
; sendiri berjalan sebagai asInvoker (lihat app.manifest) — sengaja, agar siswa
; tidak dilatih menekan "Ya" pada dialog UAC setiap kali ujian dimulai.
PrivilegesRequired=admin

; Windows 10 1809 ke atas. Anti-screenshot penuh baru aktif di build 19041,
; tetapi aplikasi tetap berjalan dan menurunkan proteksi itu jadi peringatan.
MinVersion=10.0.17763
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

OutputDir=..\dist
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
ShowLanguageDialog=no

; Aktifkan bila Anda sudah punya berkas ikon.
; SetupIconFile=ExamPC.ico

[Languages]
Name: "id"; MessagesFile: "compiler:Default.isl"

[Messages]
id.WelcomeLabel2=Program ini akan memasang [name/ver] pada komputer Anda.%n%nExamPC mengunci perangkat pada satu halaman ujian. Sesi hanya dapat diakhiri dengan PIN pengawas.%n%nTutup aplikasi lain sebelum melanjutkan.

[Tasks]
Name: "desktopicon"; Description: "Buat ikon di desktop"; GroupDescription: "Pintasan:"
Name: "firewallrule"; Description: "Izinkan ExamPC menembus Windows Firewall"; GroupDescription: "Jaringan:"

[Files]
; Seluruh isi folder publish, termasuk binari native CEF di subfolder.
; Sengaja tanpa filter: melewatkan satu berkas CEF membuat aplikasi gagal
; membuka peramban, dan gagalnya baru terlihat saat ujian sudah dimulai.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Comment: "{#AppDescription}"
Name: "{group}\Hapus {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Aturan firewall keluar agar halaman ujian dan webhook log bisa dijangkau
; walau administrator lab memasang kebijakan firewall yang ketat.
Filename: "netsh"; \
  Parameters: "advfirewall firewall add rule name=""{#AppName}"" dir=out action=allow program=""{app}\{#AppExeName}"" enable=yes"; \
  Flags: runhidden waituntilterminated; \
  StatusMsg: "Mendaftarkan aturan firewall..."; \
  Tasks: firewallrule

Filename: "{app}\{#AppExeName}"; \
  Description: "Jalankan {#AppName} sekarang"; \
  Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "netsh"; \
  Parameters: "advfirewall firewall delete rule name=""{#AppName}"""; \
  Flags: runhidden; \
  RunOnceId: "HapusAturanFirewall"

[UninstallDelete]
; Log CEF dan cache yang mungkin tertinggal di folder aplikasi.
Type: filesandordirs; Name: "{app}\GPUCache"
Type: filesandordirs; Name: "{app}\blob_storage"
Type: files; Name: "{app}\debug.log"

; Perhatikan: %APPDATA%\ExamPC SENGAJA TIDAK dihapus. Di dalamnya ada log
; pelanggaran ujian, yang bisa jadi satu-satunya bukti bila ada sengketa nilai.
; Uninstall aplikasi tidak boleh menghapus bukti — hapus manual bila memang perlu.

[Code]

{ Cegah uninstall/timpa saat sesi ujian sedang berjalan. Tanpa ini, seorang
  siswa yang punya hak admin bisa mengakhiri ujiannya lewat installer. }
function IsExamRunning(): Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  if Exec('cmd.exe',
          '/C tasklist /FI "IMAGENAME eq {#AppExeName}" | find /I "{#AppExeName}"',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := (ResultCode = 0);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if IsExamRunning() then
  begin
    MsgBox('ExamPC sedang berjalan. Akhiri sesi ujian terlebih dahulu ' +
           '(ketuk bilah kepala 5 kali, lalu masukkan PIN pengawas) ' +
           'sebelum memasang versi baru.',
           mbError, MB_OK);
    Result := False;
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  if IsExamRunning() then
  begin
    MsgBox('ExamPC sedang berjalan dan tidak dapat dihapus sekarang. ' +
           'Akhiri sesi ujian terlebih dahulu.',
           mbError, MB_OK);
    Result := False;
  end;
end;
