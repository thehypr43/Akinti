// Yakalama anahtari. Ayar chrome.storage.local'da durur; masaustu uygulamasiyla
// senkron tutmaya gerek yok, cunku yakalama davranisi tamamen eklentinin isi.
const KEY = "capture";
const box = document.getElementById("capture");
const note = document.getElementById("note");

function describe(on) {
  note.textContent = on
    ? "Chrome'da başlattığın indirmeler Akıntı penceresine düşer."
    : "Chrome kendi indiriyor. Akıntı'yı sağ tık menüsünden çağırabilirsin.";
}

chrome.storage.local.get(KEY).then((v) => {
  const on = v[KEY] !== false;          // varsayılan açık
  box.checked = on;
  describe(on);
});

box.addEventListener("change", () => {
  chrome.storage.local.set({ [KEY]: box.checked });
  describe(box.checked);
});
