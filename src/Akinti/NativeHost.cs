using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Akinti;

/// <summary>
/// Chrome native messaging host.
///
/// GUVENLIK SINIRI: Mesajdan yalnizca "url", "referrer" ve "filename" okunur.
/// Ucu de yetki tasimaz: referrer sadece HTTP basligi, filename once Sanitize'dan
/// gecirilip diyalogda kullaniciya gosterilir.
///
/// Hedef klasor, arsiv cikarma ve "yonetici olarak calistir" mesajdan HICBIR ZAMAN
/// okunmaz; yalnizca kullanicinin gordugu diyalogda secilir. Eklenti ele gecirilse
/// bile bu yolla ne yonetici yetkisiyle bir sey calistirilabilir ne de dosya
/// istenen bir klasore yazdirilabilir.
/// </summary>
public static class NativeHost
{
    private const int MaxMessageBytes = 1024 * 1024; // Chrome'un kendi siniri da 1 MB

    public static void Run(string[] args)
    {
        // args[0] = chrome-extension://<id>/ , args[1] = --parent-window=... (Chrome ekler)
        var origin = args.FirstOrDefault(a => a.StartsWith("chrome-extension://", StringComparison.Ordinal));
        Log.Info("Native host calisti. Kaynak: " + (origin ?? "belirtilmemis"));

        try
        {
            using var stdin = Console.OpenStandardInput();
            using var stdout = Console.OpenStandardOutput();

            var message = ReadMessage(stdin);
            if (message is null) { Log.Warn("Native host: mesaj okunamadi."); return; }

            var (ok, reason) = Handle(message);
            WriteMessage(stdout, JsonSerializer.Serialize(new { ok, reason }));
            stdout.Flush();
        }
        catch (Exception ex)
        {
            Log.Error("Native host hatasi", ex);
        }
    }

    private static (bool Ok, string Reason) Handle(string json)
    {
        string? url, referrer, filename, cookie, userAgent;
        try
        {
            using var doc = JsonDocument.Parse(json);

            // Okunan alanlar SADECE bunlar. Hedef klasor, arsiv cikarma ve
            // "yonetici olarak calistir" mesajdan hicbir zaman okunmaz.
            url = Str(doc.RootElement, "url");

            // Referrer yalnizca HTTP Referer basligi olarak kullanilir; hicbir
            // yetki tasimaz. Bazi dosya siteleri onsuz 403 donuyor.
            referrer = Str(doc.RootElement, "referrer");
            if (referrer is not null && !UrlHelper.IsSupportedScheme(referrer)) referrer = null;

            // Dosya adi ipucu. Yetki tasimaz ve yol olarak kullanilmaz:
            // Sanitize yol ayiracini ve ".." yi temizler, kullanici diyalogda gorur.
            filename = Str(doc.RootElement, "filename");
            if (filename is not null) filename = UrlHelper.Sanitize(filename);

            // Cerez ve tarayici kimligi: yalnizca bu indirmenin HTTP basliklari
            // olarak kullanilir. Satir sonu iceren bir deger baslik enjeksiyonuna
            // yol acabilecegi icin reddedilir.
            cookie = Clean(Str(doc.RootElement, "cookie"));
            userAgent = Clean(Str(doc.RootElement, "userAgent"));
        }
        catch (JsonException)
        {
            return (false, "gecersiz mesaj");
        }

        static string? Str(JsonElement el, string name) =>
            el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() : null;

        // Bosluk ya da kontrol karakteri (CR/LF dahil) iceren bir baslik
        // degeri HTTP baslik enjeksiyonuna yol acabilir; boylesini hic gecirme.
        static string? Clean(string? v) =>
            string.IsNullOrWhiteSpace(v) || v.Any(char.IsControl) ? null : v;

        if (string.IsNullOrWhiteSpace(url))
            return (false, "url yok");

        if (!UrlHelper.IsSupportedScheme(url))
        {
            Log.Warn("Native host desteklenmeyen semayi reddetti: " + Truncate(url, 120));
            return (false, "desteklenmeyen adres");
        }

        try
        {
            // Diyalogu ayri bir surecte ac ve cik. Kalici bir kopru birakmiyoruz.
            //
            // UseShellExecute = true SART: false olsaydi diyalog, bu surecin Chrome'a
            // bagli stdin/stdout taniticilarini miras alirdi ve host cikse bile boru
            // acik kalirdi. ShellExecuteEx tanitici mirasi yapmaz.
            // Cerez komut satirindan GECMEZ: baska sureclerce okunabilir.
            // Kisa omurlu bir istek dosyasina yazilir, diyalog okuyup siler.
            CapturedRequest.SweepStale();
            var reqPath = new CapturedRequest
            {
                Url = url!,
                Referrer = referrer,
                FileName = filename,
                Cookie = cookie,
                UserAgent = userAgent,
            }.Write();

            var psi = new ProcessStartInfo
            {
                FileName = AppPaths.ExePath,
                UseShellExecute = true,
            };
            psi.ArgumentList.Add("--request");
            psi.ArgumentList.Add(reqPath);
            Process.Start(psi);
            return (true, "diyalog acildi");
        }
        catch (Exception ex)
        {
            Log.Error("Diyalog acilamadi", ex);
            return (false, "diyalog acilamadi");
        }
    }

    // ---------------- Chrome native messaging cerceve formati ----------------

    private static string? ReadMessage(Stream stdin)
    {
        var header = new byte[4];
        if (!ReadExactly(stdin, header, 4)) return null;

        var length = BitConverter.ToInt32(header, 0);
        if (length <= 0 || length > MaxMessageBytes)
        {
            Log.Warn($"Native host gecersiz mesaj uzunlugu reddetti: {length}");
            return null;
        }

        var buffer = new byte[length];
        if (!ReadExactly(stdin, buffer, length)) return null;
        return Encoding.UTF8.GetString(buffer);
    }

    private static bool ReadExactly(Stream s, byte[] buffer, int count)
    {
        var read = 0;
        while (read < count)
        {
            var n = s.Read(buffer, read, count - read);
            if (n <= 0) return false;
            read += n;
        }
        return true;
    }

    private static void WriteMessage(Stream stdout, string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        stdout.Write(BitConverter.GetBytes(payload.Length), 0, 4);
        stdout.Write(payload, 0, payload.Length);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";
}
