<#
  aria2c.exe'yi GitHub release'inden indirir, SHA256 dogrular, tools\ altina acar.
  Binary repoya commit edilmez (.gitignore).

  Kullanim:  pwsh -File scripts\fetch-aria2.ps1
             pwsh -File scripts\fetch-aria2.ps1 -PrintHashOnly
#>
[CmdletBinding()]
param(
    [switch]$PrintHashOnly,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Version = '1.37.0'
$Url     = "https://github.com/aria2/aria2/releases/download/release-$Version/aria2-$Version-win-64bit-build1.zip"
$Sha256  = '67d015301eef0b612191212d564c5bb0a14b5b9c4796b76454276a4d28d9b288'

$root    = Split-Path -Parent $PSScriptRoot
$tools   = Join-Path $root 'tools'
$target  = Join-Path $tools 'aria2c.exe'
$tmpZip  = Join-Path ([IO.Path]::GetTempPath()) "aria2-$Version.zip"

if ((Test-Path $target) -and -not $Force -and -not $PrintHashOnly) {
    Write-Host "aria2c.exe zaten var: $target  (yeniden indirmek icin -Force)"
    exit 0
}

Write-Host "Indiriliyor: $Url"
Invoke-WebRequest -Uri $Url -OutFile $tmpZip -UseBasicParsing

$actual = (Get-FileHash -Path $tmpZip -Algorithm SHA256).Hash.ToLowerInvariant()

if ($PrintHashOnly) {
    Write-Host "SHA256 = $actual"
    Remove-Item $tmpZip -Force
    exit 0
}

if ($Sha256 -eq 'PLACEHOLDER') {
    Remove-Item $tmpZip -Force
    throw "Beklenen SHA256 scripte yazilmamis. Once -PrintHashOnly ile calistirip degeri `$Sha256'a yaz."
}

if ($actual -ne $Sha256.ToLowerInvariant()) {
    Remove-Item $tmpZip -Force
    throw "SHA256 uyusmadi. Beklenen $Sha256, gelen $actual. Indirme reddedildi."
}

New-Item -ItemType Directory -Force -Path $tools | Out-Null
$extract = Join-Path ([IO.Path]::GetTempPath()) "aria2-extract-$([guid]::NewGuid().ToString('N'))"
Expand-Archive -Path $tmpZip -DestinationPath $extract -Force

$exe = Get-ChildItem -Path $extract -Filter 'aria2c.exe' -Recurse | Select-Object -First 1
if (-not $exe) { throw "Arsivin icinde aria2c.exe bulunamadi." }

Copy-Item -Path $exe.FullName -Destination $target -Force
Remove-Item $extract -Recurse -Force
Remove-Item $tmpZip -Force

Write-Host "Hazir: $target"
& $target --version | Select-Object -First 1
