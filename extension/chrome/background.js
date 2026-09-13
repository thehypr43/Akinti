// Akıntı Chrome eklentisi
//
// İki yolla çalışır:
//   1. Sağ tık menüsü — her zaman açık.
//   2. İndirme yakalama — Chrome'un başlattığı indirmeyi iptal edip Akıntı'ya devreder.
//      Eklenti simgesine tıklayıp kapatılabilir.
//
// BU DOSYANIN YAPMADIKLARI - bilerek ve kalıcı olarak:
//   * host_permissions YOK          -> hiçbir sayfanın içeriği okunamaz
//   * content script YOK            -> sayfaya buton/banner/overlay enjekte edilmez
//   * clipboard dinleme YOK         -> kopyaladığın link indirme başlatmaz
//   * notifications YOK             -> toast çıkmaz
//   * cookies / webRequest YOK
//
// "downloads" izni sadece indirmeyi görmeye ve iptal etmeye yarar; sayfa okuma
// yetkisi vermez. Chrome bunu "İndirmelerinizi yönetin" diye gösterir.

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

chrome.runtime.onInstalled.addListener(() => { buildMenus(); refreshBadge(); });
chrome.runtime.onStartup.addListener(() => { buildMenus(); refreshBadge(); });

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

chrome.downloads.onCreated.addListener(async (item) => {
  if (!(await captureEnabled())) return;

  // finalUrl yönlendirmelerden sonraki gerçek adres; yoksa url'e düş.
  const url = valid(item.finalUrl || item.url);
  if (!url) return;                       // blob:, data:, "Sayfayı kaydet" vb. Chrome'da kalsın
  if (item.state !== "in_progress") return;

  // Chrome'un indirmesini durdur, sonra listesinden de sil ki çöp kalmasın.
  chrome.downloads.cancel(item.id, () => {
    if (chrome.runtime.lastError) {
      // Çoktan bitmiş ya da iptal edilemeyen bir indirme: dokunma, Chrome'da kalsın.
      console.warn("Akıntı: indirme iptal edilemedi, Chrome'da bırakıldı.",
                   chrome.runtime.lastError.message);
      return;
    }
    chrome.downloads.erase({ id: item.id }, () => void chrome.runtime.lastError);
    send(url, item.referrer);
  });
});

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
  if (area === "local" && CAPTURE_KEY in changes) refreshBadge();
});

// ───────────────────────── Ortak ─────────────────────────

// Mesajda SADECE url ve referrer var. Hedef klasör, arşiv çıkarma ve "yönetici
// olarak çalıştır" gibi alanlar bilerek gönderilmiyor; onlar yalnızca masaüstü
// uygulamasının diyaloğunda, kullanıcının kendi eliyle seçilir.
function send(url, referrer) {
  const msg = { url };
  const ref = valid(referrer);
  if (ref) msg.referrer = ref;          // bazı dosya siteleri Referer başlığı ister

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

function extractUrl(text) {
  if (typeof text !== "string") return null;
  const m = text.match(/(?:https?|ftps?|sftp):\/\/\S+|magnet:\?\S+/i);
  return m ? m[0] : null;
}
