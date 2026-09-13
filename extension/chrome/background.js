// Akıntı Chrome eklentisi
//
// İki yolla çalışır:
//   1. Sağ tık menüsü — her zaman açık.
//   2. İndirme yakalama — Chrome'un başlattığı indirmeyi iptal edip Akıntı'ya devreder.
//      Eklenti simgesine tıklayıp kapatılabilir.
//
// BU DOSYANIN YAPMADIKLARI - bilerek ve kalıcı olarak:
//   * content script YOK   -> sayfaya buton/banner/overlay enjekte edilmez
//   * clipboard dinleme YOK -> kopyaladığın link indirme başlatmaz
//   * notifications YOK     -> toast çıkmaz
//   * webRequest YOK        -> trafik izlenmez
//
// ÇEREZ: Kullanıcının açık tercihiyle "cookies" + <all_urls> izni alındı.
// Gofile gibi siteler dosyayı yalnızca oturum çerezine karşılık veriyor;
// çerez olmadan sunucu dosya yerine HTML sayfası gönderiyor ve indirme
// sessizce çöp bir dosyaya dönüşüyordu.
//
// Çerezler SADECE indirilecek adres için okunur (chrome.cookies.getAll({url}))
// ve yalnızca o indirmenin Cookie başlığı olarak masaüstü tarafına geçer.
// Sayfa içeriği hâlâ hiçbir yerde okunmuyor.

const HOST = "com.akinti.host";
const CAPTURE_KEY = "capture";

const MENUS = [
  { id: "akinti-link",  title: "Akıntı ile indir",              contexts: ["link"] },
  { id: "akinti-image", title: "Resmi Akıntı ile indir",        contexts: ["image"] },
  { id: "akinti-media", title: "Medyayı Akıntı ile indir",      contexts: ["video", "audio"] },
  { id: "akinti-sel",   title: "Seçili adresi Akıntı ile indir", contexts: ["selection"] },
  { id: "akinti-page",  title: "Bu sayfayı Akıntı ile indir",    contexts: ["page"] },
];

function buildMenus() {
  chrome.contextMenus.removeAll(() => {
    for (const m of MENUS) chrome.contextMenus.create(m);
  });
}

chrome.runtime.onInstalled.addListener(() => { buildMenus(); refreshBadge(); applyDownloadUi(); });
chrome.runtime.onStartup.addListener(() => { buildMenus(); refreshBadge(); applyDownloadUi(); });

// Yakalama acikken Chrome'un kendi indirme balonu kapatilir: indirmeyi hemen
// iptal ettigimiz icin sag ustte bos bir balon acilip kaliyordu. Yakalama
// kapanınca ya da devir basarisiz olup indirme Chrome'a birakilinca geri acilir,
// yoksa Chrome sessizce indirir ve kullanici hicbir sey gormez.
async function applyDownloadUi() {
  try {
    const on = await captureEnabled();
    await chrome.downloads.setUiOptions({ enabled: !on });
  } catch (e) {
    console.warn("Akıntı: indirme arayüzü ayarlanamadı.", e);
  }
}

async function showDownloadUi() {
  try { await chrome.downloads.setUiOptions({ enabled: true }); }
  catch (e) { console.warn("Akıntı: indirme arayüzü geri açılamadı.", e); }
}

// ───────────────────────── Sağ tık ─────────────────────────

chrome.contextMenus.onClicked.addListener((info) => {
  const url = pickUrl(info);
  if (url) send(url, info.pageUrl);
});

function pickUrl(info) {
  switch (info.menuItemId) {
    case "akinti-link":  return valid(info.linkUrl);
    case "akinti-image":
    case "akinti-media": return valid(info.srcUrl);
    case "akinti-page":  return valid(info.pageUrl);
    case "akinti-sel":   return valid(extractUrl(info.selectionText));
    default: return null;
  }
}

// ───────────────────────── İndirme yakalama ─────────────────────────

// Sira onemli: DURAKLAT -> devret -> ancak basarili olursa iptal et.
// Once iptal edip sonra devretmeyi denemek, devir basarisiz oldugunda
// (masaustu uygulamasi kapali, kimlik kayitli degil, vb.) indirmenin
// tamamen kaybolmasina yol acar. Duraklatma Chrome'un baglantisini
// canli tutar, boylece basarisizlikta kaldigi yerden devam edebilir.
chrome.downloads.onCreated.addListener(async (item) => {
  if (!(await captureEnabled())) return;

  // finalUrl yönlendirmelerden sonraki gerçek adres; yoksa url'e düş.
  const url = valid(item.finalUrl || item.url);
  if (!url) return;                       // blob:, data:, "Sayfayı kaydet" vb. Chrome'da kalsın
  if (item.state !== "in_progress") return;

  chrome.downloads.pause(item.id, () => {
    if (chrome.runtime.lastError) {
      // Duraklatilamiyor (cok kucuk dosya coktan bitmis olabilir): dokunma.
      return;
    }

    const msg = { url };
    const ref = valid(item.referrer);
    if (ref) msg.referrer = ref;          // bazı dosya siteleri Referer başlığı ister

    // Chrome'un belirledigi dosya adi SADECE bir ipucu. Adres bir GUID ise
    // (dosya barindirma siteleri boyle yapiyor) dogru adi yalnizca Chrome bilir.
    // Masaustu tarafi bu adi temizler ve kullanici diyalogda gorup degistirebilir.
    const base = baseName(item.filename);
    if (base) msg.filename = base;

    handOver(item, msg);
  });
});

// Çerez toplama asenkron; devir mantığı tek yerde dursun diye ayrı fonksiyon.
async function handOver(item, msg) {
  Object.assign(msg, await sessionHeaders(msg.url));

  chrome.runtime.sendNativeMessage(HOST, msg, (reply) => {
    if (chrome.runtime.lastError || !reply || reply.ok !== true) {
      const why = chrome.runtime.lastError?.message || reply?.reason || "bilinmeyen sebep";
      console.warn("Akıntı devralamadı, indirme Chrome'a bırakıldı:", why);
      showDownloadUi();     // Chrome indirecek, kullanıcı görebilsin
      chrome.downloads.resume(item.id, () => void chrome.runtime.lastError);
      return;
    }
    // Devir tamam: Chrome'un kopyasını iptal et ve listesinden sil.
    chrome.downloads.cancel(item.id, () => {
      if (chrome.runtime.lastError) return;
      chrome.downloads.erase({ id: item.id }, () => void chrome.runtime.lastError);
    });
  });
}

// Adrese ait cerezler ve tarayicinin kimlik dizesi. aria2 varsayilan olarak
// kendi User-Agent'ini gonderiyor; bazi siteler buna da bakiyor.
async function sessionHeaders(url) {
  const out = {};
  try {
    const cookies = await chrome.cookies.getAll({ url });
    if (cookies.length > 0) {
      out.cookie = cookies.map((c) => `${c.name}=${c.value}`).join("; ");
    }
  } catch (e) {
    console.warn("Akıntı: çerez okunamadı, çerezsiz devam ediliyor.", e);
  }
  try {
    if (navigator?.userAgent) out.userAgent = navigator.userAgent;
  } catch { /* onemsiz */ }
  return out;
}

// ───────────────────────── Açma/kapama ─────────────────────────

// Not: bu dosyanin en ust seviyesinde tanimsiz bir chrome.* API'sine dokunmak
// service worker'i daha yuklenirken dusurur ve eklentinin TAMAMI sessizce olur
// (sag tik dahil). Bir izin eksik kalirsa tek ozellik bozulsun diye her sey
// korumali cagriliyor.
async function captureEnabled() {
  try {
    const v = await chrome.storage.local.get(CAPTURE_KEY);
    return v[CAPTURE_KEY] !== false;    // varsayılan açık
  } catch (e) {
    console.warn("Akıntı: ayar okunamadı, yakalama açık kabul edildi.", e);
    return true;
  }
}

async function refreshBadge() {
  try {
    const on = await captureEnabled();
    await chrome.action.setBadgeText({ text: on ? "" : "OFF" });
    await chrome.action.setBadgeBackgroundColor({ color: "#595D6C" });
  } catch (e) {
    console.warn("Akıntı: rozet güncellenemedi.", e);
  }
}

chrome.storage?.onChanged?.addListener((changes, area) => {
  if (area === "local" && CAPTURE_KEY in changes) { refreshBadge(); applyDownloadUi(); }
});

// ───────────────────────── Ortak ─────────────────────────

// Mesajda yalnızca url, referrer ve dosya adı ipucu var. Hedef klasör, arşiv
// çıkarma ve "yönetici olarak çalıştır" bilerek gönderilmiyor; onlar yalnızca
// masaüstü uygulamasının diyaloğunda, kullanıcının kendi eliyle seçilir.
async function send(url, referrer) {
  const msg = { url };
  const ref = valid(referrer);
  if (ref) msg.referrer = ref;          // bazı dosya siteleri Referer başlığı ister

  // Sağ tıkla indirilen bağlantı da oturum gerektirebilir; aynı başlıklar gitsin.
  Object.assign(msg, await sessionHeaders(url));

  chrome.runtime.sendNativeMessage(HOST, msg, () => {
    if (chrome.runtime.lastError) {
      console.warn("Akıntı bağlantı hatası:", chrome.runtime.lastError.message);
    }
  });
}

// Sadece gerçekten indirilebilir şemalar geçer. javascript:, data:, blob:,
// file: ve chrome: adresleri burada elenir (masaüstü tarafında bir kez daha elenir).
function valid(url) {
  if (typeof url !== "string" || url.length === 0 || url.length > 8192) return null;
  const lower = url.trim().toLowerCase();
  const ok = ["http://", "https://", "ftp://", "ftps://", "sftp://", "magnet:"];
  return ok.some((p) => lower.startsWith(p)) ? url.trim() : null;
}

// Yol degil, sadece son parca. Klasor bilgisi bilerek atiliyor: hedef klasoru
// her zaman masaustu diyalogu belirler, tarayicidan gelen mesaj degil.
function baseName(p) {
  if (typeof p !== "string" || p.length === 0) return null;
  const last = p.split(/[\\/]/).pop();     // Windows'ta ters bölü, diğerlerinde bölü
  return last && last.length > 0 && last.length <= 255 ? last : null;
}

function extractUrl(text) {
  if (typeof text !== "string") return null;
  const m = text.match(/(?:https?|ftps?|sftp):\/\/\S+|magnet:\?\S+/i);
  return m ? m[0] : null;
}
