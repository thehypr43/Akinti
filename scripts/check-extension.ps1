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

# setUiOptions ayri bir izin ister; API adindan anlasilmaz.
if ($allText -match 'setUiOptions') {
    if ($declared -notcontains 'downloads.ui') {
        $problems += "downloads.setUiOptions kullaniliyor ama 'downloads.ui' izni YOK."
    }
}

# ---- 2. Beklenen izin kumesi ----
# Amac: yeni bir izin fark edilmeden eklenemesin. Listeye bir sey eklemek
# bilincli bir karar olmali, bu yuzden degisiklik burada da gorunur olsun.
#
# cookies + <all_urls> kullanicinin acik tercihiyle eklendi: Gofile gibi
# siteler dosyayi yalnizca oturum cerezine karsilik veriyor, cerezsiz indirme
# dosya yerine HTML sayfasi kaydediyordu.
$expected = @('contextMenus', 'nativeMessaging', 'downloads', 'downloads.ui', 'storage', 'cookies')

foreach ($d in $declared) {
    if ($expected -notcontains $d) {
        $problems += "'$d' izni beklenmiyor. Bilerek ekledinse check-extension.ps1 icindeki listeyi guncelle."
    }
}

$expectedHosts = @('<all_urls>')
$hosts = @()
if ($manifest.PSObject.Properties.Name -contains 'host_permissions') { $hosts = @($manifest.host_permissions) }
foreach ($h in $hosts) {
    if ($expectedHosts -notcontains $h) {
        $problems += "host_permissions icinde beklenmeyen giris: '$h'."
    }
}

# Bunlar hala kesinlikle disarida: sayfaya kod enjekte etmek ve trafigi izlemek
# hicbir zaman gerekmedi.
if ($manifest.PSObject.Properties.Name -contains 'content_scripts') {
    $problems += "content_scripts eklenmis. Sayfaya kod enjekte edilmemesi sozu bozuluyor."
}
foreach ($banned in 'webRequest', 'webRequestBlocking', 'clipboardRead', 'history', 'bookmarks') {
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
