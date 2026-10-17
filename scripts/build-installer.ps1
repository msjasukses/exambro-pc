<#
.SYNOPSIS
    Membangun ExamPC dan mengemasnya menjadi installer Windows.

.DESCRIPTION
    Tiga tahap: publish self-contained, verifikasi hasilnya, lalu kompilasi
    installer dengan Inno Setup. Hasil akhir: dist\ExamPC-Setup-<versi>.exe

.PARAMETER Version
    Versi yang ditulis ke installer dan ke daftar Program & Fitur Windows.

.PARAMETER SkipPublish
    Lewati dotnet publish dan langsung kemas folder publish yang sudah ada.
    Berguna saat menyetel skrip installer tanpa menunggu build ulang.

.EXAMPLE
    .\scripts\build-installer.ps1 -Version 1.0.3
#>

[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.3',

    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$RepoRoot   = Split-Path -Parent $PSScriptRoot
$PublishDir = Join-Path $RepoRoot 'publish\windows'
$DistDir    = Join-Path $RepoRoot 'dist'
$IssFile    = Join-Path $RepoRoot 'installer\ExamPC.iss'

function Write-Step { param([string]$Text) Write-Host "`n=== $Text ===" -ForegroundColor Cyan }
function Write-Ok   { param([string]$Text) Write-Host "  OK  $Text" -ForegroundColor Green }
function Write-Warn { param([string]$Text) Write-Host "  !   $Text" -ForegroundColor Yellow }

# ---- Tahap 0: periksa perkakas ---------------------------------------------

Write-Step 'Memeriksa perkakas'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet tidak ditemukan. Pasang .NET SDK 8: winget install Microsoft.DotNet.SDK.8`n" +
          "Lalu tutup dan buka ulang terminal agar PATH terbaca."
}
Write-Ok ".NET SDK $(dotnet --version)"

# ISCC.exe tidak masuk PATH secara otomatis, jadi periksa lokasi bakunya.
$IsccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 7\ISCC.exe"
)

$Iscc = $IsccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $Iscc) {
    $Iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
}
if (-not $Iscc) {
    throw "ISCC.exe (Inno Setup) tidak ditemukan. Pasang dengan:`n" +
          "  winget install JRSoftware.InnoSetup"
}
Write-Ok "Inno Setup: $Iscc"

# ---- Tahap 1: publish -------------------------------------------------------

if ($SkipPublish) {
    Write-Step 'Melewati publish (-SkipPublish)'
    if (-not (Test-Path $PublishDir)) {
        throw "Folder publish belum ada: $PublishDir`nJalankan sekali tanpa -SkipPublish."
    }
} else {
    Write-Step "Publish self-contained win-x64 (versi $Version)"

    # Folder lama dibersihkan supaya berkas dari build sebelumnya tidak ikut
    # terkemas — installer yang membawa DLL usang gagalnya sulit dilacak.
    if (Test-Path $PublishDir) {
        Remove-Item $PublishDir -Recurse -Force
        Write-Ok 'Folder publish lama dibersihkan'
    }

    dotnet publish (Join-Path $RepoRoot 'src\ExamPC.App\ExamPC.App.csproj') `
        -c Release `
        -r win-x64 `
        --self-contained `
        -p:Version=$Version `
        -p:AssemblyVersion="$Version.0" `
        -p:FileVersion="$Version.0" `
        -p:DebugType=none `
        -o $PublishDir

    if ($LASTEXITCODE -ne 0) { throw "dotnet publish gagal dengan kode $LASTEXITCODE." }
    Write-Ok 'Publish selesai'
}

# ---- Tahap 2: verifikasi hasil publish --------------------------------------

Write-Step 'Memverifikasi hasil publish'

$ExePath = Join-Path $PublishDir 'ExamPC.exe'
if (-not (Test-Path $ExePath)) {
    throw "ExamPC.exe tidak ada di $PublishDir. Periksa <AssemblyName> di ExamPC.App.csproj."
}
Write-Ok 'ExamPC.exe ditemukan'

# CEF wajib lengkap. Kalau salah satu hilang, aplikasi tetap terpasang dan tetap
# terbuka — lalu gagal memuat halaman ujian saat siswa sudah duduk di depan PC.
$RequiredCef = @('libcef.dll', 'icudtl.dat')
$MissingCef = $RequiredCef | Where-Object { -not (Test-Path (Join-Path $PublishDir $_)) }

if ($MissingCef) {
    Write-Warn "Berkas CEF tidak ditemukan: $($MissingCef -join ', ')"
    Write-Warn 'Ini berarti paket peramban tertanam belum ter-restore dengan benar.'
    Write-Warn 'Installer akan tetap dibuat, tetapi peramban ujian kemungkinan gagal terbuka.'
} else {
    Write-Ok 'Binari CEF lengkap'
}

$SizeMb = [math]::Round((Get-ChildItem $PublishDir -Recurse -File |
                         Measure-Object -Property Length -Sum).Sum / 1MB, 1)
Write-Ok "Ukuran folder publish: $SizeMb MB"

# ---- Tahap 3: kompilasi installer -------------------------------------------

Write-Step 'Mengompilasi installer'

if (-not (Test-Path $DistDir)) { New-Item -ItemType Directory -Path $DistDir | Out-Null }

& $Iscc `
    "/DAppVersion=$Version" `
    "/DPublishDir=$PublishDir" `
    $IssFile

if ($LASTEXITCODE -ne 0) { throw "Kompilasi Inno Setup gagal dengan kode $LASTEXITCODE." }

$SetupPath = Join-Path $DistDir "ExamPC-Setup-$Version.exe"
if (-not (Test-Path $SetupPath)) {
    throw "Installer tidak ditemukan di $SetupPath meski ISCC melaporkan sukses."
}

$SetupMb = [math]::Round((Get-Item $SetupPath).Length / 1MB, 1)

Write-Step 'Selesai'
Write-Host "  Installer : $SetupPath" -ForegroundColor Green
Write-Host "  Ukuran    : $SetupMb MB" -ForegroundColor Green
Write-Host ""
Write-Host "  Pasang di satu PC   : klik dua kali installer-nya" -ForegroundColor Gray
Write-Host "  Pasang massal (lab) : ExamPC-Setup-$Version.exe /VERYSILENT /NORESTART /SUPPRESSMSGBOXES" -ForegroundColor Gray
Write-Host ""
Write-Warn 'Installer ini belum ditandatangani secara digital. Windows SmartScreen'
Write-Warn 'akan menampilkan peringatan pada beberapa PC pertama. Lihat README §9.'
