<#
.SYNOPSIS
    Baska bilgisayarlara goturulecek kurulum paketini hazirlar.

.DESCRIPTION
    Paket kendi kendine yeter: uygulama onceden derlenmis (self-contained, .NET
    kurulumu gerekmez), aria2c.exe icinde, eklenti yaninda. Hedef makinede
    internet ya da .NET SDK aranmaz.

    Klasor yapisi bilerek deponunkiyle ayni: install.ps1 -SkipBuild publish\,
    tools\ ve extension\ klasorlerini kendi yaninda arar.

.PARAMETER OutDir
    Paketin yazilacagi yer. Varsayilan: Masaustu\Akinti-Kurulum
#>
[CmdletBinding()]
param(
    [string]$OutDir = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Akinti-Kurulum')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = Split-Path -Parent $PSScriptRoot
function Step($m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Ok($m)   { Write-Host "    $m" -ForegroundColor DarkGray }

Step 'Uygulama derleniyor (self-contained)'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK bulunamadi. Paketi hazirlayan makinede gerekli: winget install Microsoft.DotNet.SDK.10"
}
$pub = Join-Path $RepoRoot 'publish'
dotnet publish (Join-Path $RepoRoot 'src\Akinti\Akinti.csproj') -c Release -o $pub --nologo
if ($LASTEXITCODE -ne 0) { throw 'Derleme basarisiz.' }

$aria2 = Join-Path $RepoRoot 'tools\aria2c.exe'
if (-not (Test-Path $aria2)) {
    Step 'aria2c.exe indiriliyor'
    & (Join-Path $PSScriptRoot 'fetch-aria2.ps1')
}

Step "Paket yaziliyor: $OutDir"
if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path `
    $OutDir,
    (Join-Path $OutDir 'publish'),
    (Join-Path $OutDir 'tools'),
    (Join-Path $OutDir 'scripts'),
    (Join-Path $OutDir 'extension\chrome') | Out-Null

Copy-Item (Join-Path $pub '*')  (Join-Path $OutDir 'publish') -Recurse -Force
Copy-Item $aria2                (Join-Path $OutDir 'tools\aria2c.exe') -Force
Copy-Item (Join-Path $RepoRoot 'extension\chrome\*') (Join-Path $OutDir 'extension\chrome') -Recurse -Force

foreach ($f in 'install.ps1', 'uninstall.ps1', 'check-extension.ps1', 'fetch-aria2.ps1') {
    Copy-Item (Join-Path $PSScriptRoot $f) (Join-Path $OutDir 'scripts') -Force
}

# Cift tiklanabilir baslaticilar. install.ps1 PowerShell 7 sozdizimi kullaniyor
# (ConvertFrom-Json -AsHashtable), o yuzden pwsh yoksa kurulum durur ve ne
# yapilacagini soyler.
$kur = @'
@echo off
setlocal
cd /d "%~dp0"
where pwsh >nul 2>nul
if errorlevel 1 (
  echo PowerShell 7 gerekiyor.
  echo Once sunu calistir:  winget install Microsoft.PowerShell
  pause
  exit /b 1
)
pwsh -NoProfile -ExecutionPolicy Bypass -File "scripts\install.ps1" -SkipBuild
pause
'@
Set-Content (Join-Path $OutDir 'KUR.cmd') $kur -Encoding ascii

$kaldir = @'
@echo off
setlocal
cd /d "%~dp0"
pwsh -NoProfile -ExecutionPolicy Bypass -File "scripts\uninstall.ps1"
pause
'@
Set-Content (Join-Path $OutDir 'KALDIR.cmd') $kaldir -Encoding ascii

$readme = @"
Akıntı - kurulum paketi
=======================

Bu klasör kendi kendine yeter: uygulama derlenmiş halde, aria2 motoru içinde.
Hedef bilgisayarda .NET kurmana ya da internete bağlanmana gerek yok.

KURULUM
-------
1. KUR.cmd dosyasına çift tıkla.
   (PowerShell 7 yoksa önce:  winget install Microsoft.PowerShell)

2. Chrome eklentisini yükle:
     chrome://extensions  ->  Geliştirici modu (sağ üst)  ->  Paketlenmemiş öğeyi yükle
     Şu klasörü seç:  %LOCALAPPDATA%\Programs\Akinti\extension

3. KUR.cmd'ye bir kez daha çift tıkla.
   Chrome eklentiye kendi kimliğini verir; ikinci çalıştırma o kimliği okuyup
   masaüstü uygulamasıyla konuşma iznini ona bağlar. Bu adım atlanırsa eklenti
   "Access to the specified native messaging host is forbidden" der.

KALDIRMA
--------
KALDIR.cmd. Ayarları saklamak için:  pwsh -File scripts\uninstall.ps1 -KeepData
İndirilmiş dosyalara hiçbir durumda dokunulmaz.

NE NEREDE
---------
Uygulama : %LOCALAPPDATA%\Programs\Akinti
Ayarlar  : %APPDATA%\Akinti\config.json
Günlük   : %APPDATA%\Akinti\akinti.log
"@
Set-Content (Join-Path $OutDir 'KURULUM.txt') $readme -Encoding utf8

$size = (Get-ChildItem $OutDir -Recurse -File | Measure-Object -Property Length -Sum).Sum
Ok ("Paket hazir: {0}  ({1:N0} MB)" -f $OutDir, ($size / 1MB))
