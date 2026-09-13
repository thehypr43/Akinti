<#
.SYNOPSIS
    HafifDM'i kaldirir.

.PARAMETER KeepData
    Ayarlari ve indirme gecmisini (%APPDATA%\HafifDM) silme.

.NOTES
    Indirilen dosyalara hicbir sekilde dokunulmaz.
#>
[CmdletBinding()]
param([switch]$KeepData)

$ErrorActionPreference = 'Continue'

$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\HafifDM'
$DataDir    = Join-Path $env:APPDATA 'HafifDM'
$TaskName   = 'HafifDM Engine'
$HostName   = 'com.hafifdm.host'

function Step($m) { Write-Host "==> $m" -ForegroundColor Cyan }

Step 'Motor durduruluyor'
Get-Process aria2c  -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process HafifDM -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Step 'Zamanlanmis gorev siliniyor'
Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue

Step 'Native messaging kaydi siliniyor'
Remove-Item -Path "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$HostName" `
            -Recurse -Force -ErrorAction SilentlyContinue

Step 'Guvenlik duvari kurali siliniyor'
Remove-NetFirewallRule -DisplayName 'HafifDM aria2 (gelen engelle)' -ErrorAction SilentlyContinue

Step 'Program dosyalari siliniyor'
Start-Sleep -Milliseconds 500
Remove-Item -Path $InstallDir -Recurse -Force -ErrorAction SilentlyContinue

if ($KeepData) {
    Write-Host "    Ayarlar ve gecmis korundu: $DataDir" -ForegroundColor DarkGray
} else {
    Step 'Ayarlar ve gecmis siliniyor'
    Remove-Item -Path $DataDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host 'Kaldirildi. Indirilen dosyalara dokunulmadi.' -ForegroundColor Green
Write-Host 'Chrome eklentisini chrome://extensions sayfasindan elle kaldirmayi unutma.' -ForegroundColor Yellow
