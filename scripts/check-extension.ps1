<#
.SYNOPSIS
    Eklentiyi kurmadan once iki seyi dogrular.

.DESCRIPTION
    1. KULLANILAN HER chrome.* API'si icin manifest'te izin var mi?
       MV3'te service worker'in en ust seviyesinde tanimsiz bir API'ye dokunmak
       worker'i yuklenirken dusurur ve eklentinin TAMAMI sessizce olur - sag tik
       menusu dahil. Hicbir hata penceresi cikmaz, sadece hicbir sey calismaz.
       (Bu kontrol, storage izninin unutulup her seyin durmasi uzerine yazildi.)

    2. Verilen sozler duruyor mu: sayfa okuma izni, content script, cerez,
       webRequest ya da pano erisimi eklenmis mi?

    Kurulum bu script basarisiz olursa durur.
#>
[CmdletBinding()]
param([string]$ExtDir)

$ErrorActionPreference = 'Stop'

if (-not $ExtDir) { $ExtDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'extension\chrome' }

$manifestPath = Join-Path $ExtDir 'manifest.json'
if (-not (Test-Path $manifestPath)) { throw "manifest.json bulunamadi: $manifestPath" }

$manifest = Get-Content $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json
$declared = @($manifest.permissions)
$problems = @()

# ---- 1. Izin gerektiren API'ler ----
# Izin ADI gerektirmeyenler (runtime, action, i18n) bilerek listede yok.
$needsPermission = @{
    'contextMenus'  = 'contextMenus'
    'downloads'     = 'downloads'
    'storage'       = 'storage'
    'notifications' = 'notifications'
    'cookies'       = 'cookies'
    'webRequest'    = 'webRequest'
    'scripting'     = 'scripting'
    'history'       = 'history'
    'bookmarks'     = 'bookmarks'
    'clipboardRead' = 'clipboardRead'
}

$scripts = Get-ChildItem $ExtDir -Filter '*.js' -File
$used = [System.Collections.Generic.HashSet[string]]::new()

foreach ($f in $scripts) {
    $text = Get-Content $f.FullName -Raw -Encoding utf8
    foreach ($m in [regex]::Matches($text, 'chrome\.([A-Za-z]+)')) {
        [void]$used.Add($m.Groups[1].Value)
    }
}

foreach ($api in $used) {
    if ($needsPermission.ContainsKey($api)) {
        $perm = $needsPermission[$api]
        if ($declared -notcontains $perm) {
            $problems += "chrome.$api kullaniliyor ama manifest'te '$perm' izni YOK. " +
                         "Service worker yuklenirken coker, eklentinin tamami calismaz."
        }
    }
}

# nativeMessaging API adiyla degil, dogrudan izin adiyla kullanilir
$allText = ($scripts | ForEach-Object { Get-Content $_.FullName -Raw -Encoding utf8 }) -join "`n"
if ($allText -match 'sendNativeMessage|connectNative') {
    if ($declared -notcontains 'nativeMessaging') {
        $problems += "Native messaging kullaniliyor ama 'nativeMessaging' izni YOK."
    }
}

# ---- 2. Verilen sozler ----
if ($manifest.PSObject.Properties.Name -contains 'host_permissions') {
    $problems += "host_permissions eklenmis. Eklentinin sayfalari okuyamamasi sozu bozuluyor."
}
if ($manifest.PSObject.Properties.Name -contains 'content_scripts') {
    $problems += "content_scripts eklenmis. Sayfaya kod enjekte edilmemesi sozu bozuluyor."
}
foreach ($banned in 'cookies', 'webRequest', 'clipboardRead', 'tabs', '<all_urls>') {
    if ($declared -contains $banned) {
        $problems += "'$banned' izni eklenmis. Bu izin bilerek disarida birakilmisti."
    }
}

# ---- Sonuc ----
if ($problems.Count -gt 0) {
    Write-Host ''
    Write-Host 'EKLENTI KONTROLU BASARISIZ' -ForegroundColor Red
    foreach ($p in $problems) { Write-Host "  - $p" -ForegroundColor Red }
    Write-Host ''
    throw "Eklenti kontrolu $($problems.Count) sorun buldu."
}

Write-Host "    eklenti kontrolu tamam (izinler: $($declared -join ', '))" -ForegroundColor DarkGray
