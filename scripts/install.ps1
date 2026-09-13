<#
.SYNOPSIS
    HafifDM kurulumu. Yonetici gerektirmez (guvenlik duvari kurali haric, o atlanabilir).

.DESCRIPTION
    Sirasiyla:
      1. Uygulamayi %LOCALAPPDATA%\Programs\HafifDM altina kopyalar
      2. aria2 RPC icin RASTGELE port + RASTGELE 32 baytlik parola uretir
      3. config.json yazar ve dosyanin erisimini sadece bu kullaniciya kisitlar
      4. aria2.conf uretir (uygulamanin kendisine urettirir, tek kaynak)
      5. Motoru gizli baslatan start-engine.vbs dosyasini yazar
      6. Oturum acilisinda calisan zamanlanmis gorevi olusturur
      7. Chrome native messaging host'unu kaydeder
      8. (Yonetici ise) aria2c icin gelen baglantilari engelleyen guvenlik duvari kurali ekler

.PARAMETER ExtensionId
    Chrome eklenti kimligi. Verilmezse eklentinin kurulacagi yoldan hesaplanir
    (Chrome paketlenmemis eklentilere yolun SHA-256'sindan sabit bir kimlik verir).

.EXAMPLE
    pwsh -File scripts\install.ps1
#>
[CmdletBinding()]
param(
    [string]$ExtensionId,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot   = Split-Path -Parent $PSScriptRoot
$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\HafifDM'
$DataDir    = Join-Path $env:APPDATA 'HafifDM'
$ExtDir     = Join-Path $InstallDir 'extension'
$TaskName   = 'HafifDM Engine'
$HostName   = 'com.hafifdm.host'

function Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "    $msg" -ForegroundColor DarkGray }
function Warn2($m)  { Write-Host "    ! $m" -ForegroundColor Yellow }

# ---------------------------------------------------------------- 1. Derle ve kopyala
if (-not $SkipBuild) {
    Step 'Uygulama derleniyor'
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw ".NET SDK bulunamadi. Once kur:  winget install Microsoft.DotNet.SDK.10"
    }
    $proj = Join-Path $RepoRoot 'src\HafifDM\HafifDM.csproj'
    $pub  = Join-Path $RepoRoot 'publish'
    dotnet publish $proj -c Release -o $pub --nologo
    if ($LASTEXITCODE -ne 0) { throw "Derleme basarisiz." }
} else {
    $pub = Join-Path $RepoRoot 'publish'
}

$aria2 = Join-Path $RepoRoot 'tools\aria2c.exe'
if (-not (Test-Path $aria2)) {
    Step 'aria2c.exe indiriliyor'
    & (Join-Path $PSScriptRoot 'fetch-aria2.ps1')
}

Step "Dosyalar kopyalaniyor: $InstallDir"
New-Item -ItemType Directory -Force -Path $InstallDir, $DataDir, (Join-Path $InstallDir 'tools') | Out-Null

# Motor calisiyorsa exe'ler kilitli olur; once durdur.
Get-Process aria2c -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process HafifDM -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400

Copy-Item -Path (Join-Path $pub '*') -Destination $InstallDir -Recurse -Force
Copy-Item -Path $aria2 -Destination (Join-Path $InstallDir 'tools\aria2c.exe') -Force

New-Item -ItemType Directory -Force -Path $ExtDir | Out-Null
Copy-Item -Path (Join-Path $RepoRoot 'extension\chrome\*') -Destination $ExtDir -Recurse -Force

$ExePath = Join-Path $InstallDir 'HafifDM.exe'
if (-not (Test-Path $ExePath)) { throw "HafifDM.exe kopyalanamadi." }
Ok "HafifDM.exe hazir"

# ---------------------------------------------------------------- 2. Port + parola
Step 'RPC portu ve parolasi uretiliyor'

$ConfigFile = Join-Path $DataDir 'config.json'
$existing = if (Test-Path $ConfigFile) { Get-Content $ConfigFile -Raw | ConvertFrom-Json } else { $null }

function Test-PortFree([int]$p) {
    try {
        $l = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $p)
        $l.Start(); $l.Stop(); return $true
    } catch { return $false }
}

# Varsayilan 6800 BILEREK kullanilmiyor: bir saldiri scripti ilk onu dener.
$port = 0
if ($existing -and $existing.RpcPort -gt 0 -and (Test-PortFree $existing.RpcPort)) {
    $port = [int]$existing.RpcPort
    Ok "Mevcut port korundu: $port"
} else {
    for ($i = 0; $i -lt 200; $i++) {
        $p = Get-Random -Minimum 49152 -Maximum 65535
        if ($p -ne 6800 -and (Test-PortFree $p)) { $port = $p; break }
    }
    if ($port -eq 0) { throw "Bos bir port bulunamadi." }
    Ok "Rastgele port secildi: $port"
}

if ($existing -and $existing.RpcSecret) {
    $secret = [string]$existing.RpcSecret
    Ok 'Mevcut RPC parolasi korundu'
} else {
    $bytes = [byte[]]::new(32)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    # base64url: aria2 komut satirinda ve HTTP'de sorunsuz tasinsin
    $secret = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    Ok 'Yeni 32 baytlik RPC parolasi uretildi'
}

# ---------------------------------------------------------------- 3. config.json
Step 'config.json yaziliyor'

if ($existing) {
    $existing.RpcPort   = $port
    $existing.RpcSecret = $secret
    $cfg = $existing
} else {
    $cfg = [pscustomobject]@{
        RpcPort     = $port
        RpcSecret   = $secret
        DownloadDir = (Join-Path $env:USERPROFILE 'Downloads')
    }
}
$cfg | ConvertTo-Json -Depth 10 | Set-Content -Path $ConfigFile -Encoding utf8

# Parola bu dosyada duruyor: erisimi sadece bu kullaniciya kisitla.
$acl = Get-Acl $ConfigFile
$acl.SetAccessRuleProtection($true, $false)   # miras alinan kurallari kaldir
$acl.Access | ForEach-Object { [void]$acl.RemoveAccessRule($_) }
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    [System.Security.Principal.WindowsIdentity]::GetCurrent().User,
    'FullControl', 'Allow')
$acl.AddAccessRule($rule)
Set-Acl -Path $ConfigFile -AclObject $acl
Ok 'config.json sadece bu kullaniciya acik'

# ---------------------------------------------------------------- 4. aria2.conf
Step 'aria2.conf uretiliyor'
& $ExePath --write-conf | Out-Null
Start-Sleep -Milliseconds 600
$ConfPath = Join-Path $DataDir 'aria2.conf'
if (-not (Test-Path $ConfPath)) { throw "aria2.conf uretilemedi. $DataDir\hafifdm.log dosyasina bak." }
Ok $ConfPath

# ---------------------------------------------------------------- 5. Gizli baslatici
Step 'start-engine.vbs yaziliyor'
$VbsPath  = Join-Path $DataDir 'start-engine.vbs'
$Aria2Exe = Join-Path $InstallDir 'tools\aria2c.exe'

# WScript.Shell.Run(..., 0, False): pencere gostermez, beklemez.
# wscript.exe hemen cikar, geriye sadece aria2c.exe kalir.
@"
' HafifDM - motoru konsol penceresi gostermeden baslatir.
' Bu dosya scripts\install.ps1 tarafindan uretildi.
Dim sh
Set sh = CreateObject("WScript.Shell")
sh.Run """$Aria2Exe"" --conf-path=""$ConfPath""", 0, False
"@ | Set-Content -Path $VbsPath -Encoding ascii
Ok $VbsPath

# ---------------------------------------------------------------- 6. Zamanlanmis gorev
Step 'Zamanlanmis gorev olusturuluyor'
$action    = New-ScheduledTaskAction -Execute 'wscript.exe' -Argument "//nologo `"$VbsPath`""
$trigger   = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$trigger.Delay = 'PT30S'
$settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                                          -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) `
                                          -MultipleInstances IgnoreNew
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive `
                                        -RunLevel Limited

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger `
                       -Settings $settings -Principal $principal -Force `
                       -Description 'HafifDM indirme motorunu (aria2c) arka planda baslatir.' | Out-Null
Ok "Gorev: $TaskName (oturum acilisinda, 30 sn gecikmeli, gizli)"

# ---------------------------------------------------------------- 7. Chrome native messaging
Step 'Chrome native messaging host kaydediliyor'

function Get-UnpackedExtensionId([string]$path) {
    # Chrome paketlenmemis eklentiye, mutlak yolun SHA-256'sinin ilk 16 baytindan
    # sabit bir kimlik uretir. Her nibble 'a'..'p' harflerine esleniyor.
    $normalized = $path.TrimEnd('\')
    if ($normalized -match '^[a-z]:') {
        $normalized = $normalized.Substring(0,1).ToUpper() + $normalized.Substring(1)
    }
    $bytes  = [System.Text.Encoding]::Unicode.GetBytes($normalized)
    $hash   = [System.Security.Cryptography.SHA256]::HashData($bytes)
    $sb     = [System.Text.StringBuilder]::new()
    for ($i = 0; $i -lt 16; $i++) {
        [void]$sb.Append([char](97 + ($hash[$i] -shr 4)))
        [void]$sb.Append([char](97 + ($hash[$i] -band 0x0F)))
    }
    return $sb.ToString()
}

if (-not $ExtensionId) {
    $ExtensionId = Get-UnpackedExtensionId $ExtDir
    Ok "Eklenti kimligi yoldan hesaplandi: $ExtensionId"
} else {
    Ok "Eklenti kimligi elle verildi: $ExtensionId"
}

$manifest = [ordered]@{
    name            = $HostName
    description     = 'HafifDM indirme koprusu'
    path            = $ExePath
    type            = 'stdio'
    # SADECE bu eklenti bu host ile konusabilir.
    allowed_origins = @("chrome-extension://$ExtensionId/")
}
$ManifestPath = Join-Path $InstallDir 'native-host.json'
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $ManifestPath -Encoding utf8

New-Item -Path "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$HostName" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$HostName" `
                 -Name '(Default)' -Value $ManifestPath
Ok "Kayit: HKCU\Software\Google\Chrome\NativeMessagingHosts\$HostName"

# ---------------------------------------------------------------- 8. Guvenlik duvari
Step 'Guvenlik duvari kurali'
$isAdmin = ([System.Security.Principal.WindowsPrincipal]
            [System.Security.Principal.WindowsIdentity]::GetCurrent()
           ).IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)

if ($isAdmin) {
    Remove-NetFirewallRule -DisplayName 'HafifDM aria2 (gelen engelle)' -ErrorAction SilentlyContinue
    New-NetFirewallRule -DisplayName 'HafifDM aria2 (gelen engelle)' -Direction Inbound `
                        -Program $Aria2Exe -Action Block -Profile Any -RemoteAddress Any `
                        -ErrorAction SilentlyContinue | Out-Null
    Ok 'aria2c icin gelen baglantilar engellendi'
} else {
    Warn2 'Yonetici degilsin, guvenlik duvari kurali atlandi.'
    Warn2 'Zaten aria2 sadece 127.0.0.1 dinliyor ve parola ile kilitli; bu kural fazladan bir katmandi.'
    Warn2 'Istersen bu scripti yonetici PowerShell ile bir kez daha calistir.'
}

# ---------------------------------------------------------------- 9. Motoru baslat
Step 'Motor baslatiliyor'
Start-Process -FilePath 'wscript.exe' -ArgumentList "//nologo `"$VbsPath`"" -WindowStyle Hidden
Start-Sleep -Seconds 2
if (Get-Process aria2c -ErrorAction SilentlyContinue) {
    Ok 'aria2c.exe calisiyor (Gorev Yoneticisi > Ayrintilar altinda gorunur)'
} else {
    Warn2 "aria2c baslamadi. $DataDir\hafifdm.log dosyasina bak."
}

# ---------------------------------------------------------------- Ozet
Write-Host ''
Write-Host 'Kurulum tamam.' -ForegroundColor Green
Write-Host ''
Write-Host 'Sirada senin yapacagin tek sey - Chrome eklentisini yukle:' -ForegroundColor White
Write-Host '  1. Chrome > chrome://extensions'
Write-Host '  2. Sag ustten "Gelistirici modu" (Developer mode) ac'
Write-Host '  3. "Paketlenmemis ogeyi yukle" (Load unpacked) tikla'
Write-Host "  4. Su klasoru sec:  $ExtDir"
Write-Host ''
Write-Host "  Beklenen eklenti kimligi: $ExtensionId" -ForegroundColor DarkGray
Write-Host '  Chrome farkli bir kimlik gosterirse su komutu o kimlikle bir kez daha calistir:' -ForegroundColor DarkGray
Write-Host "    pwsh -File scripts\install.ps1 -SkipBuild -ExtensionId <kimlik>" -ForegroundColor DarkGray
Write-Host ''
Write-Host 'Kontrol:' -ForegroundColor White
Write-Host "  Uygulama : $ExePath"
Write-Host "  Ayarlar  : $ConfigFile"
Write-Host "  Gunluk   : $(Join-Path $DataDir 'hafifdm.log')"
Write-Host "  RPC portu: $port  (sadece 127.0.0.1, parola korumali)"
