// HafifDM Chrome eklentisi
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

const HOST = "com.hafifdm.host";

const MENUS = [
  { id: "hafifdm-link",  title: "HafifDM ile indir",          contexts: ["link"] },
  { id: "hafifdm-image", title: "Resmi HafifDM ile indir",    contexts: ["image"] },
  { id: "hafifdm-media", title: "Medyayi HafifDM ile indir",  contexts: ["video", "audio"] },
  { id: "hafifdm-sel",   title: "Secili adresi HafifDM ile indir", contexts: ["selection"] },
  { id: "hafifdm-page",  title: "Bu sayfayi HafifDM ile indir",    contexts: ["page"] },
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
      console.warn("HafifDM baglanti hatasi:", chrome.runtime.lastError.message);
    }
  });
});

function pickUrl(info) {
  switch (info.menuItemId) {
    case "hafifdm-link":  return valid(info.linkUrl);
    case "hafifdm-image":
    case "hafifdm-media": return valid(info.srcUrl);
    case "hafifdm-page":  return valid(info.pageUrl);
    case "hafifdm-sel":   return valid(extractUrl(info.selectionText));
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
