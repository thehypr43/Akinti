// Akinti Chrome eklentisi
//
// BU DOSYANIN YAPMADIKLARI - bilerek ve kalici olarak:
//   * chrome.downloads YOK        -> tarayicinin kendi indirmelerine dokunulmaz
//   * content script YOK          -> sayfalara buton/banner/overlay enjekte edilmez
//   * host_permissions YOK        -> hicbir sayfanin icerigi okunamaz
//   * clipboard dinleme YOK       -> kopyaladigin link indirme baslatmaz
//   * notifications YOK           -> toast cikmaz
//   * webRequest / medya sniffer YOK
//
// Tek is: sag tik menusune birkac madde koymak ve tiklaninca adresi masaustu
// uygulamasina yollamak. Bir web sitesi senin adina sag tik menusu tiklayamaz,
// dolayisiyla bu yoldan habersiz indirme baslatilamaz.

const HOST = "com.akinti.host";

const MENUS = [
  { id: "akinti-link",  title: "Akıntı ile indir",          contexts: ["link"] },
  { id: "akinti-image", title: "Resmi Akıntı ile indir",    contexts: ["image"] },
  { id: "akinti-media", title: "Medyayı Akıntı ile indir",  contexts: ["video", "audio"] },
  { id: "akinti-sel",   title: "Seçili adresi Akıntı ile indir", contexts: ["selection"] },
  { id: "akinti-page",  title: "Bu sayfayı Akıntı ile indir",    contexts: ["page"] },
];

function buildMenus() {
  chrome.contextMenus.removeAll(() => {
    for (const m of MENUS) chrome.contextMenus.create(m);
  });
}

chrome.runtime.onInstalled.addListener(buildMenus);
chrome.runtime.onStartup.addListener(buildMenus);

chrome.contextMenus.onClicked.addListener((info) => {
  const url = pickUrl(info);
  if (!url) return;

  // Mesajda SADECE url var. Hedef klasor, arsiv cikarma ve "yonetici olarak
  // calistir" gibi alanlar bilerek gonderilmiyor; onlar yalnizca masaustu
  // uygulamasinin diyalogunda, kullanicinin kendi eliyle secilir.
  chrome.runtime.sendNativeMessage(HOST, { url }, () => {
    if (chrome.runtime.lastError) {
      console.warn("Akıntı bağlantı hatası:", chrome.runtime.lastError.message);
    }
  });
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

// Sadece gercekten indirilebilir semalar gecer. javascript:, data:, blob:,
// file: ve chrome: adresleri burada elenir (masaustu tarafinda bir kez daha elenir).
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
