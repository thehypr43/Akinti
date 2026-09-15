using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Akinti.Ui;

/// <summary>
/// Windows 11'in kendi gorunumu: renkler kayittan okunur, tema (acik/koyu) ve
/// vurgu rengi sistemin ne dediyse odur. Boylece uygulama isletim sistemine
/// gomulu gibi durur, kendi kararini dayatmaz.
///
/// Paletin tamami burada URETILIR, Theme.xaml'de degil: alfa harmanlama ve
/// vurgu renginin acik/koyu temaya gore aydinlatilmasi XAML'de yazilamiyor.
/// Pencere XAML'leri firca anahtarlarini DynamicResource ile aldigi icin
/// kullanici Windows temasini degistirince acik pencereler de aninda doner.
/// </summary>
public static class ThemeManager
{
    private const string PersonalizeKey =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static ResourceDictionary? _current;

    public static bool IsLight { get; private set; }

    public static void Initialize()
    {
        Apply();

        // Kullanici Windows'ta temayi ya da vurgu rengini degistirdiginde
        // acik pencereler de donsun.
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
                Application.Current?.Dispatcher.Invoke(Apply);
        };
    }

    public static void Apply()
    {
        var app = Application.Current;
        if (app is null) return;

        var dict = Build(ReadIsLight(), ReadAccent());

        if (_current is not null) app.Resources.MergedDictionaries.Remove(_current);
        // Palet en sona eklenir: ayni anahtar birden fazla sozlukte varsa
        // WPF sonuncuyu kullanir, boylece Theme.xaml'deki her sey bunu gorur.
        app.Resources.MergedDictionaries.Add(dict);
        _current = dict;
    }

    // ──────────── Sistemden okuma ────────────

    private static bool ReadIsLight()
    {
        // Kullanicinin Windows temasini kurcalamadan iki modu da denemek icin
        // bir kacis kapisi: AKINTI_THEME=light|dark
        var forced = Environment.GetEnvironmentVariable("AKINTI_THEME");
        if (string.Equals(forced, "light", StringComparison.OrdinalIgnoreCase)) return IsLight = true;
        if (string.Equals(forced, "dark", StringComparison.OrdinalIgnoreCase)) return IsLight = false;

        var v = Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", null);
        IsLight = v is int i && i != 0;
        return IsLight;
    }

    /// <summary>
    /// Windows'un vurgu rengi. DWM onu ABGR duzeninde bir DWORD olarak tutuyor.
    /// Okunamazsa tasarimin kendi mavisi kullanilir.
    /// </summary>
    private static Color? ReadAccent()
    {
        try
        {
            var v = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null);
            if (v is not int abgr) return null;
            return Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
        }
        catch { return null; }
    }

    // ──────────── Palet ────────────

    /// <summary>
    /// Palet, Claude Design'daki "Fluent İndirme Yöneticisi" dosyasindan aliniyor;
    /// vurgu rengi ise kullanicinin Windows'ta sectigi renk. Sistemin ham rengi
    /// oldugu gibi kullanilamiyor: acik temada cok aydinlik bir vurgunun uzerindeki
    /// beyaz yazi okunmuyor, koyu temada cok karanligi zemine gomuluyor. Bu yuzden
    /// renk tonu korunup yalnizca aydinligi temaya gore hizalaniyor.
    /// </summary>
    private static ResourceDictionary Build(bool light, Color? systemAccent)
    {
        var d = new ResourceDictionary();

        void C(string key, Color c) => d[key] = c;
        void B(string key, Color c) => d[key] = Frozen(c);
        void Alpha(string key, Color c, double a) => d[key] = Frozen(WithAlpha(c, a));

        // --- Tasarim belirteçleri ---
        // Koyu temada vurgu, aydinlik bir tonda birakilip uzerine siyah yazi
        // yazilabilirdi; ama acik mavi zeminde siyah yazi cirkin duruyor. Bunun
        // yerine dolgu biraz koyulasiyor ve yazi iki temada da beyaz kaliyor.
        var accent = systemAccent is { } sys
            ? WithLightness(sys, light ? 0.36 : 0.50)
            : (light ? Rgb(0x0F6CBD) : Rgb(0x3A8FC4));            // --accent
        var accentInk  = Colors.White;
        var desk       = light ? Rgb(0xECEFF3) : Rgb(0x171717);   // --desk
        var card       = light ? Rgb(0xFFFFFF) : Rgb(0x2D2D2D);   // --card
        var ink        = light ? Rgb(0x191919) : Rgb(0xFFFFFF);   // --ink
        var ink2       = light ? Rgb(0x5C5C5C) : Rgb(0xC5C5C5);   // --ink2
        var ink3       = light ? Rgb(0x8A8A8A) : Rgb(0x909090);   // --ink3
        var ok         = light ? Rgb(0x0F7B0F) : Rgb(0x6CCB5F);   // --ok
        var err        = light ? Rgb(0xC42B1C) : Rgb(0xFF99A4);   // --err
        var warn       = light ? Rgb(0x9D5D00) : Rgb(0xFCE100);   // --warn
        var onSurface  = light ? Colors.Black  : Colors.White;

        C("C.Page", desk); C("C.Bg", desk); C("C.Surface", card);
        C("C.Text", ink); C("C.Accent", accent); C("C.Accent2", accent);
        C("C.Danger", err); C("C.Success", ok);
        C("C.Neutral100", ink);
        C("C.Neutral700", ink3);
        C("C.Neutral800", light ? Rgb(0xD6D6D6) : Rgb(0x3A3A3A));

        // --mica: pencere zemini duz renk degil, hafif bir degrade.
        var mica = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 1),
            GradientStops =
            {
                new GradientStop(light ? Rgb(0xF7F8FA) : Rgb(0x2A2A2E), 0.00),
                new GradientStop(light ? Rgb(0xEEF1F6) : Rgb(0x232326), 0.55),
                new GradientStop(light ? Rgb(0xF2F0F5) : Rgb(0x272429), 1.00),
            },
        };
        mica.Freeze();
        d["B.Mica"] = mica;

        B("B.Page", desk);
        B("B.Bg", desk);
        B("B.Surface", card);
        B("B.Text", ink);
        B("B.TextMuted", ink2);
        B("B.TextDim", ink2);
        B("B.TextFaint", ink3);
        B("B.Accent", accent);
        B("B.Accent2", accent);
        B("B.AccentText", accentInk);
        B("B.Danger", err);
        B("B.Success", ok);
        B("B.Warn", warn);

        // "Yonetici olarak calistir" tonu: uyari sarisi degil, sakin bir turuncu.
        var admin = light ? Rgb(0x9A4F00) : Rgb(0xE9A66A);
        B("B.Admin", admin);
        Alpha("B.AdminFill", admin, light ? 0.10 : 0.14);
        Alpha("B.AdminHover", admin, light ? 0.17 : 0.22);
        Alpha("B.AdminStroke", admin, light ? 0.32 : 0.36);

        // --- Katmanlar ve cizgiler ---
        Alpha("B.Card2", Colors.White, light ? 0.55 : 0.04);          // --card2
        Alpha("B.Layer", Colors.White, light ? 0.70 : 0.045);         // --layer
        Alpha("B.Neutral800", onSurface, 0.075);                      // --stroke
        Alpha("B.Neutral700", onSurface, 0.12);                       // --stroke2
        Alpha("B.ControlStroke", onSurface, 0.12);                    // --stroke2
        Alpha("B.Divider", onSurface, light ? 0.055 : 0.06);          // --divider
        Alpha("B.TrackDim", onSurface, light ? 0.09 : 0.14);          // --track
        B("B.ControlFill", card);

        // --- Etkilesim ---
        Alpha("B.HoverText", onSurface, light ? 0.037 : 0.055);       // --hover
        Alpha("B.HoverSubtle", onSurface, light ? 0.028 : 0.04);
        Alpha("B.ZebraFill", onSurface, light ? 0.02 : 0.03);
        Alpha("B.Press", onSurface, light ? 0.06 : 0.09);             // --press
        Alpha("B.InputHover", onSurface, light ? 0.30 : 0.36);

        Alpha("B.HoverAccent", accent, 0.14);
        Alpha("B.GhostHover", accent, 0.10);
        Alpha("B.SparkFill", accent, 0.20);
        Alpha("B.AccentSubtle", accent, light ? 0.12 : 0.18);
        Alpha("B.DangerHover", err, light ? 0.14 : 0.26);

        // Dolu birincil dugmenin altindaki ikinci sira: ayni renk ailesi,
        // dusuk vurgu. Parlak degil, zemine yedirilmis bir ton.
        Alpha("B.SoftFill", accent, light ? 0.10 : 0.14);
        Alpha("B.SoftHover", accent, light ? 0.17 : 0.22);
        Alpha("B.SoftStroke", accent, light ? 0.30 : 0.34);
        B("B.SoftText", WithLightness(accent, light ? 0.30 : 0.74));

        return d;
    }

    // ──────────── Renk yardimcilari ────────────

    private static Color Rgb(int hex) =>
        Color.FromRgb((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);

    /// <summary>Rengin tonunu ve doygunlugunu koruyup aydinligini hedefe cekar.</summary>
    private static Color WithLightness(Color c, double target)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2.0, h = 0, s = 0;

        if (max > min)
        {
            var d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6.0;
        }

        // Cok soluk bir vurgu (kullanici gri secmis olabilir) aydinlatilinca
        // tamamen gri kaliyor; ton yoksa dokunmaya da deger yok.
        l = Math.Clamp(target, 0, 1);
        if (s <= 0.001) return Color.FromRgb((byte)(l * 255), (byte)(l * 255), (byte)(l * 255));

        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        return Color.FromRgb(Ch(p, q, h + 1.0 / 3), Ch(p, q, h), Ch(p, q, h - 1.0 / 3));

        static byte Ch(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            var v = t < 1.0 / 6 ? p + (q - p) * 6 * t
                  : t < 1.0 / 2 ? q
                  : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6
                  : p;
            return (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        }
    }

    private static Color WithAlpha(Color c, double a) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

}
