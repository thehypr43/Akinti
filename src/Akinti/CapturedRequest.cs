using System.Text.Json;

namespace Akinti;

/// <summary>
/// Tarayicidan devralinan indirmenin verisi.
///
/// NEDEN DOSYA, NEDEN KOMUT SATIRI DEGIL: Cerez basligi oturum jetonu tasiyor.
/// Windows'ta bir surecin komut satiri makinedeki diger sureclerden okunabiliyor
/// (Gorev Yoneticisi, WMI, Process Explorer), dolayisiyla cerezi arguman olarak
/// gecmek onu sizdirmak olurdu. Bunun yerine %APPDATA%\Akinti altina - ki o
/// klasor zaten yalnizca bu kullaniciya acik - kisa omurlu bir dosya yazilir,
/// yolu arguman olarak verilir ve diyalog dosyayi okur okumaz siler.
/// </summary>
public sealed class CapturedRequest
{
    public string Url { get; set; } = "";
    public string? Referrer { get; set; }
    public string? FileName { get; set; }

    /// <summary>"ad=deger; ad2=deger2" — dogrudan Cookie basligi olarak gider.</summary>
    public string? Cookie { get; set; }

    /// <summary>Tarayicinin kimlik dizesi; bazi siteler aria2'nin kendi UA'sini reddediyor.</summary>
    public string? UserAgent { get; set; }

    private static readonly JsonSerializerOptions Opts = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Diske yazar ve yolunu doner.</summary>
    public string Write()
    {
        AppPaths.EnsureDataDir();
        var path = Path.Combine(AppPaths.DataDir, $"req-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(this, Opts));
        return path;
    }

    /// <summary>Okur ve dosyayi HER DURUMDA siler; cerez diskte oyalanmasin.</summary>
    public static CapturedRequest? ReadAndDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<CapturedRequest>(File.ReadAllText(path), Opts);
        }
        catch (Exception ex)
        {
            Log.Error("Devralinan istek okunamadi", ex);
            return null;
        }
        finally
        {
            try { File.Delete(path); } catch { /* onemsiz */ }
        }
    }

    // ──────────── Diyalogun karari ────────────
    //
    // Yakalanan indirme Chrome'da DURAKLATILMIS halde bekliyor. Native host,
    // diyalog bir karar verene kadar Chrome'a cevap vermiyor; karar buradaki
    // kucuk dosya uzerinden geciyor:
    //
    //   "browser" -> host Chrome'a "devralamadim" der, Chrome indirmeyi surdurur
    //   bunun disinda -> devir tamam, Chrome kendi kopyasini iptal eder
    //
    // Boylece "Tarayiciya birak" gercekten tarayiciya birakir: dosya bastan
    // indirilmez, duraklatilmis indirme kaldigi yerden devam eder.

    public const string LeaveToBrowser = "browser";

    private static string DecisionPath(string requestPath) => requestPath + ".decision";

    /// <summary>Diyalogun kararini yazar. Istek dosyasi yoksa (elle indirme) sessizce gecer.</summary>
    public static void WriteDecision(string? requestPath, string decision)
    {
        if (string.IsNullOrEmpty(requestPath)) return;
        try { File.WriteAllText(DecisionPath(requestPath), decision); }
        catch (Exception ex) { Log.Warn("Karar yazilamadi: " + ex.Message); }
    }

    /// <summary>Karar dosyasini bekler ve okur; sure dolarsa bos doner.</summary>
    public static string WaitForDecision(string requestPath, TimeSpan timeout)
    {
        var path = DecisionPath(requestPath);
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (File.Exists(path))
                {
                    var v = File.ReadAllText(path).Trim();
                    File.Delete(path);
                    return v;
                }
            }
            catch { /* diyalog tam o anda yaziyor olabilir; bir tur daha bekle */ }
            Thread.Sleep(120);
        }
        return "";
    }

    /// <summary>
    /// Karar hala duruyorsa kimse dinlemiyor demektir (host suresi dolmus ya da
    /// cokmus). O zaman Chrome'un kopyasi coktan iptal edilmistir; indirmeyi
    /// tarayiciya birakmanin tek yolu adresi tarayicida yeniden acmaktir.
    /// </summary>
    public static bool DecisionPending(string? requestPath) =>
        !string.IsNullOrEmpty(requestPath) && File.Exists(DecisionPath(requestPath));

    /// <summary>
    /// Cokmus bir diyalogdan kalmis eski istek dosyalarini temizler.
    /// Cerez iceren dosyalar diskte birikmesin.
    /// </summary>
    public static void SweepStale()
    {
        try
        {
            if (!Directory.Exists(AppPaths.DataDir)) return;
            var cutoff = DateTime.Now.AddHours(-1);
            foreach (var f in Directory.EnumerateFiles(AppPaths.DataDir, "req-*"))
            {
                try { if (File.GetLastWriteTime(f) < cutoff) File.Delete(f); }
                catch { /* onemsiz */ }
            }
        }
        catch { /* onemsiz */ }
    }
}
