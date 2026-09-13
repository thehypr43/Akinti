# Akinti

Windows için hafif indirme yöneticisi. Tray ikonu yok, tarayıcıda gezerken önüne çıkan "indir" butonu yok, panoyu dinlemez.

Arka planda sadece **tek bir küçük süreç** durur: `aria2c.exe` (~10-15 MB). Pencereyi kapattığında geriye hiçbir şey kalmaz.

## Ne yapar

- **Sağ tık menüsünden indirme.** Chrome'da bir bağlantıya sağ tıkla, "Akıntı ile indir" de. Başka hiçbir yerde karşına çıkmaz.
- **Dosya türüne göre klasörleme.** `.zip` Arşivler'e, `.mp4` Video'ya, `.exe` Programlar'a. Kurallar ayarlardan düzenlenir.
- **İndirince arşivi çıkar.** 7-Zip, NanaZip veya WinRAR ile, arşiv adını taşıyan bir alt klasöre.
- **İndirince yönetici olarak çalıştır.** Kurulum dosyaları için. UAC onayı Windows tarafından sorulur.
- **Torrent.** Magnet ve `.torrent`, DHT/PEX destekli, torrent içinden dosya seçebilirsin.
- **Devam eden / tamamlanan / başarısız** listeleri, duraklat-devam-iptal, hız limiti, bağlantı sayısı.

## Rahatsız etmeme garantileri

Bunlar tasarım kararı, ayar değil. Kapatılabilir bir şey değiller çünkü kodda hiç yoklar:

| Yapmadığı şey | Neden önemli |
|---|---|
| Content script enjekte etmez | Sayfaya buton, banner, medya sniffer yapıştırmaz |
| Panoyu dinlemez | Kopyaladığın link indirme başlatmaz |
| Bildirim göstermez | Toast çıkmaz |
| `webRequest` kullanmaz | Trafiğin izlenmez |
| Protokol handler kaydetmez | Hiçbir site `akinti://...` ile uygulamayı tetikleyemez |

Her indirme, senin gördüğün bir diyalogdan açık onay ister. Yakalama eklenti
simgesinden kapatılabilir; sağ tık menüsü her hâlükârda çalışır.

`scripts/check-extension.ps1` bu listeyi kurulumda zorlar: beklenmeyen bir izin
ya da content script eklenirse kurulum durur.

## Eklentinin istediği izinler

| İzin | Ne için |
|---|---|
| `contextMenus` | Sağ tık menüsü |
| `nativeMessaging` | Masaüstü uygulamasına devretmek |
| `downloads` | Chrome'un indirmesini duraklatıp devralmak |
| `downloads.ui` | Devralınca Chrome'un boş indirme balonunu gizlemek |
| `storage` | Yakalama anahtarının durumu |
| `cookies` + `<all_urls>` | **Oturum çerezlerini indirmeye taşımak** |

Son satır bilinçli bir taviz. Gofile, Mega gibi siteler dosyayı yalnızca oturum
çerezine karşılık veriyor; çerez olmadan sunucu dosya yerine kendi HTML sayfasını
gönderiyor ve indirme sessizce 3 KB'lık çöp bir dosyaya dönüşüyor. Bunun bedeli
Chrome'un eklenti sayfasında "tüm web sitelerindeki verilerinizi okuyabilir"
yazması.

Çerez yalnızca **indirilecek adres için** okunur (`chrome.cookies.getAll({url})`)
ve yalnızca o indirmenin `Cookie` başlığı olarak geçer. Sayfa içeriği hâlâ
hiçbir yerde okunmuyor — content script yok.

Çerezin komut satırından geçmediğine dikkat: Windows'ta bir sürecin komut satırı
makinedeki diğer süreçlerden okunabiliyor. Onun yerine `%APPDATA%\Akinti` altına
kısa ömürlü bir istek dosyası yazılır, diyalog okur okumaz siler.

## Güvenlik

Motor (`aria2c`) kendisini kontrol edebilmek için `127.0.0.1` üzerinde bir port dinler. Kilitleri:

1. Kurulumda üretilen **32 baytlık rastgele parola** (`--rpc-secret`). Parolasız her istek reddedilir.
2. **Sadece loopback** (`--rpc-listen-all=false`). Ağdan erişilemez.
3. **Rastgele yüksek port** (49152-65535). aria2'nin bilinen 6800 portu bilerek kullanılmaz.
4. `config.json` dosyasının erişimi sadece kuran kullanıcıya kısıtlanır (parola orada durur).
5. Yönetici varsa: `aria2c.exe` için gelen bağlantıları engelleyen güvenlik duvarı kuralı.
6. Chrome 130+'ın Private Network Access koruması, bir web sayfasının `127.0.0.1`'e istek atmasını zaten engeller.

**Yetki sınırı:** Native messaging üzerinden eklentiden gelen mesajdan sadece `url` okunur. Hedef klasör, arşiv çıkarma ve "yönetici olarak çalıştır" alanları mesajdan **hiçbir zaman** okunmaz; yalnızca diyalogda senin elinle seçilir. Eklenti ele geçirilse bile bu yoldan yönetici yetkisiyle bir şey çalıştırılamaz.

### Kendin doğrula

Kurulumdan sonra Chrome'da herhangi bir siteyi aç, DevTools konsoluna yapıştır (portu Ayarlar > Güvenlik bölümünden al):

```js
fetch('http://127.0.0.1:PORT/jsonrpc', {
  method: 'POST',
  body: '{"jsonrpc":"2.0","id":"1","method":"aria2.getVersion"}'
}).then(r => r.text()).then(console.log).catch(e => console.log('engellendi:', e.message));
```

Sonuç hata veya yetkilendirme reddi olmalı. Sürüm bilgisi dönerse bana haber ver.

## Kurulum

Ön koşul: .NET SDK 10.

```bash
winget install Microsoft.DotNet.SDK.10
```

Sonra:

```bash
pwsh -File scripts/install.ps1
```

Script şunları yapar: derler, `%LOCALAPPDATA%\Programs\Akinti` altına kurar, aria2c'yi indirip SHA256 doğrular, rastgele port + parola üretir, açılışta çalışan gizli bir zamanlanmış görev kurar, Chrome native messaging host'unu kaydeder.

Güvenlik duvarı kuralı için yönetici gerekir; yönetici değilsen o adım atlanır ve script sana söyler. Diğer her şey yönetici olmadan çalışır.

### Chrome eklentisi

Script bittikten sonra tek elle adım:

1. `chrome://extensions`
2. Sağ üstten **Geliştirici modu**nu aç
3. **Paketlenmemiş öğeyi yükle**
4. `%LOCALAPPDATA%\Programs\Akinti\extension` klasörünü seç

Chrome'un gösterdiği eklenti kimliği script'in yazdırdığından farklıysa, script'i o kimlikle bir kez daha çalıştır:

```bash
pwsh -File scripts/install.ps1 -SkipBuild -ExtensionId <kimlik>
```

## Kaldırma

```bash
pwsh -File scripts/uninstall.ps1
```

Ayarları ve geçmişi korumak için `-KeepData` ekle. İndirilmiş dosyalara hiçbir durumda dokunulmaz. Chrome eklentisini `chrome://extensions` sayfasından elle kaldır.

## Yapı

```
src/Akinti/          Tek WPF projesi, tek exe, argümana göre çok mod
  Aria2Client.cs        JSON-RPC istemcisi
  Aria2Engine.cs        aria2.conf üretimi, gizli başlatma
  Archivers.cs          7-Zip / NanaZip / WinRAR tespiti ve çağrısı
  PostActions.cs        İndirme sonrası: çıkar, yönetici çalıştır, klasör aç
  NativeHost.cs         Chrome köprüsü (güvenlik sınırı burada)
  Ui/                   Pencereler + Theme.xaml
extension/chrome/     MV3 eklenti (sağ tık + yakalama + çerez devri)
scripts/              install / uninstall / fetch-aria2 / make-icons
```

`Akinti.exe` argümanına göre davranır:

| Argüman | Mod |
|---|---|
| (boş) | Ana pencere |
| `--new <url>` | İndirme diyaloğu |
| `chrome-extension://...` | Chrome native messaging host |
| `<gid> <n> <yol>` | aria2 indirme-sonrası kancası |
| `--write-conf` | config.json'dan aria2.conf üret |

## Günlük

Sorun olursa: `%APPDATA%\Akinti\akinti.log`

## Tasarım

Arayüz renkleri, tipografi ve köşe yarıçapları `src/Akinti/Ui/Theme.xaml` içinde token olarak durur. Tasarım değişikliği pencere XAML'lerine dokunmadan oradan yapılır.
