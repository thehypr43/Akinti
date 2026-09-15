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

        var dict = Build(ReadIsLight(), default);

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

    // ──────────── Palet ────────────

    /// <summary>
    /// Palet, Claude Design'daki "Fluent İndirme Yöneticisi" dosyasindan birebir
    /// aliniyor. Vurgu rengi de tasarimin kendi rengi: Windows'un vurgu rengi
    /// mor/pembe secilmis olabiliyor ve tasarimin mavisiyle cakisıyordu. Acik/koyu
    /// secimi yine Windows'tan geliyor.
    /// </summary>
    private static ResourceDictionary Build(bool light, Color systemAccent)
    {
        var d = new ResourceDictionary();

        void C(string key, Color c) => d[key] = c;
        void B(string key, Color c) => d[key] = Frozen(c);
        void Alpha(string key, Color c, double a) => d[key] = Frozen(WithAlpha(c, a));

        // --- Tasarim belirteçleri ---
        var accent     = light ? Rgb(0x0F6CBD) : Rgb(0x4CC2FF);   // --accent
        var accentInk  = light ? Colors.White  : Rgb(0x00243B);   // --accentink
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

}
