using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Akinti.Ui;

/// <summary>
/// Pencere cercevesi DWM'e birakilir: yuvarlatilmis koseler, ince kenar cizgisi
/// ve baslik cubugu rengi. Bunlar WPF'ten cizilemiyor, pencerenin kendi
/// bolgesine ve istemci disi alanina uygulaniyor.
///
/// Baslik cubugu Windows'un kendi cubugu - kapat/kucult/buyut dugmeleri, cift
/// tiklayip buyutme, Snap yerlesimleri, dokunmatik ve ekran okuyucu davranisi
/// hepsi bedava gelir. Elle cizilmis dugmeler bunlarin hicbirini vermiyordu.
/// Cubuk, pencere zemininin rengine boyanarak govdeyle ayni parcaymis gibi durur.
/// </summary>
public static class WindowEffects
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Pencere olusturulurken cagrilir; tutamac hazir olunca uygulanir.</summary>
    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) => Apply(window);
    }

    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        try
        {
            var round = DWMWCP_ROUND;
            var hr = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            if (hr != 0) Log.Warn($"Kose yuvarlatma uygulanamadi (hr=0x{hr:X8})");

            var dark = ThemeManager.IsLight ? 0 : 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

            var border = ToColorRef(window.TryFindResource("B.Neutral700") as SolidColorBrush,
                                    ThemeManager.IsLight ? 0x00C8C8C8 : 0x00454545);
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));

            // Baslik cubugu govdenin rengini alsin ki pencere tek parca gorunsun.
            var caption = ToColorRef(window.TryFindResource("B.Bg") as SolidColorBrush,
                                     ThemeManager.IsLight ? 0x00F9F9F9 : 0x00202020);
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));

            var captionText = ThemeManager.IsLight ? 0x00404040 : 0x00C8C8C8;
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref captionText, sizeof(int));
        }
        catch (Exception ex)
        {
            // Eski bir Windows surumunde bu oznitelikler yok; pencere koseli kalir,
            // uygulama calismaya devam eder.
            Log.Warn("Pencere cercevesi ayarlanamadi: " + ex.Message);
        }
    }

    /// <summary>DWM COLORREF bekliyor: 0x00BBGGRR. Firca saydamsa duz bir ton kullanilir.</summary>
    private static int ToColorRef(SolidColorBrush? brush, int fallback)
    {
        if (brush is null) return fallback;
        var c = brush.Color;
        if (c.A < 200) return fallback;
        return c.R | (c.G << 8) | (c.B << 16);
    }
}
