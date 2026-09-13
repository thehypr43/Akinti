<#
.SYNOPSIS
    Akıntı'yı kaldırır.

.PARAMETER KeepData
    Ayarlari ve indirme gecmisini (%APPDATA%\Akinti) silme.

.NOTES
    Indirilen dosyalara hicbir sekilde dokunulmaz.
#>
[CmdletBinding()]
param([switch]$KeepData)

$ErrorActionPreference = 'Continue'

$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\Akinti'
$DataDir    = Join-Path $env:APPDATA 'Akinti'
$TaskName   = 'Akıntı Motoru'
$HostName   = 'com.akinti.host'

function Step($m) { Write-Host "==> $m" -ForegroundColor Cyan }

Step 'Motor durduruluyor'
Get-Process aria2c  -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process Akinti -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Step 'Zamanlanmis gorev siliniyor'
Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue

Step 'Native messaging kaydi siliniyor'
Remove-Item -Path "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$HostName" `
            -Recurse -Force -ErrorAction SilentlyContinue

Step 'Guvenlik duvari kurali siliniyor'
Remove-NetFirewallRule -DisplayName 'Akıntı aria2 (gelen engelle)' -ErrorAction SilentlyContinue

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
