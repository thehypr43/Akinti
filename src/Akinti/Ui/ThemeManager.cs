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
    private const string DwmKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM";

    /// <summary>Windows varsayilan vurgusu; kayit okunamazsa bu kullanilir.</summary>
    private static readonly Color FallbackAccent = Color.FromRgb(0x00, 0x78, 0xD4);

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

    private static Color ReadAccent()
    {
        // DWM\AccentColor ABGR duzeninde bir DWORD tutuyor.
        if (Registry.GetValue(DwmKey, "AccentColor", null) is int abgr)
        {
            var r = (byte)(abgr & 0xFF);
            var g = (byte)((abgr >> 8) & 0xFF);
            var b = (byte)((abgr >> 16) & 0xFF);
            if (r + g + b > 0) return Color.FromRgb(r, g, b);
        }
        return FallbackAccent;
    }

    // ──────────── Palet ────────────

    private static ResourceDictionary Build(bool light, Color systemAccent)
    {
        // Vurgu rengi olduğu gibi kullanilamaz: koyu zeminde Windows kendi
        // vurgusunun acik tonunu, acik zeminde koyu tonunu kullaniyor.
        var accent = WithLightness(systemAccent, light ? 0.36 : 0.66);
        var accent2 = WithLightness(systemAccent, light ? 0.46 : 0.76);

        var text = light ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White;
        var onSurface = light ? Colors.Black : Colors.White;

        var d = new ResourceDictionary();

        void C(string key, Color c) => d[key] = c;
        void B(string key, Color c) => d[key] = Frozen(c);
        void Alpha(string key, Color c, double a) => d[key] = Frozen(WithAlpha(c, a));

        // --- Zeminler ---
        var page = light ? Rgb(0xF3F3F3) : Rgb(0x1B1B1B);
        var bg = light ? Rgb(0xF9F9F9) : Rgb(0x202020);
        var surface = light ? Rgb(0xFFFFFF) : Rgb(0x2B2B2B);

        C("C.Page", page); C("C.Bg", bg); C("C.Surface", surface);
        C("C.Text", text); C("C.Accent", accent); C("C.Accent2", accent2);
        C("C.Danger", light ? Rgb(0xC42B1C) : Rgb(0xFF99A4));
        C("C.Success", light ? Rgb(0x0F7B0F) : Rgb(0x6CCB5F));
        C("C.Neutral100", text);
        C("C.Neutral700", light ? Rgb(0x8A8A8A) : Rgb(0x6E6E6E));
        C("C.Neutral800", light ? Rgb(0xD6D6D6) : Rgb(0x3A3A3A));

        B("B.Page", page);
        B("B.Bg", bg);
        B("B.Surface", surface);
        B("B.Text", text);
        B("B.Accent", accent);
        B("B.Accent2", accent2);
        B("B.Danger", (Color)d["C.Danger"]);
        B("B.Success", (Color)d["C.Success"]);

        // --- Cizgiler ve kenarliklar (Win11: metin renginin dusuk alfasi) ---
        Alpha("B.Neutral800", onSurface, light ? 0.09 : 0.10);   // kart cercevesi
        Alpha("B.Neutral700", onSurface, light ? 0.16 : 0.18);
        Alpha("B.Divider", onSurface, light ? 0.08 : 0.09);
        Alpha("B.ControlStroke", onSurface, light ? 0.11 : 0.09);
        B("B.ControlFill", light ? Rgb(0xFDFDFD) : Rgb(0x2F2F2F));

        // --- Metin katmanlari ---
        Alpha("B.TextMuted", onSurface, light ? 0.72 : 0.78);
        Alpha("B.TextDim", onSurface, light ? 0.62 : 0.68);
        Alpha("B.TextFaint", onSurface, light ? 0.55 : 0.58);

        // --- Uzerine gelme ---
        Alpha("B.HoverText", onSurface, light ? 0.055 : 0.08);
        Alpha("B.HoverSubtle", onSurface, light ? 0.035 : 0.05);
        Alpha("B.ZebraFill", onSurface, light ? 0.025 : 0.035);
        Alpha("B.InputHover", onSurface, light ? 0.30 : 0.36);
        Alpha("B.TrackDim", onSurface, light ? 0.14 : 0.16);

        Alpha("B.HoverAccent", accent, 0.14);
        Alpha("B.GhostHover", accent, 0.10);
        Alpha("B.SparkFill", accent, 0.20);
        Alpha("B.AccentSubtle", accent, light ? 0.10 : 0.16);
        Alpha("B.DangerHover", (Color)d["C.Danger"], light ? 0.16 : 0.28);

        // Vurgu dolgusu uzerindeki yazi: koyu temada vurgu acik oldugu icin siyah.
        B("B.AccentText", light ? Colors.White : Colors.Black);

        return d;
    }

    // ──────────── Renk yardimcilari ────────────

    private static Color Rgb(int hex) =>
        Color.FromRgb((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);

    private static Color WithAlpha(Color c, double a) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>Rengin tonunu ve doygunlugunu koruyup parlakligini hedefe tasir.</summary>
    private static Color WithLightness(Color c, double targetL)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, h = 0, s = 0;

        if (max > min)
        {
            var delta = max - min;
            s = l > 0.5 ? delta / (2 - max - min) : delta / (max + min);
            if (max == r) h = (g - b) / delta + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / delta + 2;
            else h = (r - g) / delta + 4;
            h /= 6;
        }

        // Cok soluk bir vurgu rengi koyu zeminde kayboluyor; taban doygunlugu ver.
        s = Math.Max(s, 0.35);
        l = Math.Clamp(targetL, 0, 1);

        if (s == 0) return Color.FromRgb((byte)(l * 255), (byte)(l * 255), (byte)(l * 255));

        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        return Color.FromRgb(To(Hue(p, q, h + 1.0 / 3)), To(Hue(p, q, h)), To(Hue(p, q, h - 1.0 / 3)));

        static byte To(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);

        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
    }
}
