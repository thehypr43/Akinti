using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Akinti.Ui;

/// <summary>
/// Indirme bitince ekranin ortasinda acilan sonuc penceresi. Ilerleme penceresi
/// kendini kapatip yerine bunu acar, boylece "iniyor" ile "bitti" ayni pencerede
/// karisik durmaz.
///
/// Basarili sonuc bir sure sonra kendiliginden kapanir; hata kalir, cunku
/// okunmasi gereken durum odur. Fare pencereye girdiginde sayac iptal edilir:
/// kullanici okurken pencere altindan kaybolmasin.
/// </summary>
public partial class DoneWindow : Window
{
    private const int AutoCloseSeconds = 20;

    private readonly string _filePath;
    private DispatcherTimer? _autoClose;

    public DoneWindow(DownloadItem item, TimeSpan? elapsed = null)
    {
        InitializeComponent();
        WindowEffects.Attach(this);

        _filePath = PostActions.ResolveFilePath(item.FilePath, item.Dir, item.DisplayName);
        var failed = item.Status == Aria2Status.Error;

        IcoFile.Text = Icons.ForFile(_filePath.Length > 0 ? _filePath : item.DisplayName, item.IsTorrent);
        TxtName.Text = item.DisplayName;

        var host = string.IsNullOrEmpty(item.SourceUri) ? ""
            : Uri.TryCreate(item.SourceUri, UriKind.Absolute, out var u) ? u.Host : "";

        TxtSource.Text = host.Length > 0 ? host : "—";
        TxtLocation.Text = _filePath.Length > 0 ? Path.GetDirectoryName(_filePath) ?? "—" : "—";
        TxtLocation.ToolTip = TxtLocation.Text;

        var size = item.TotalLength > 0 ? item.TotalLength : SafeLength(_filePath);
        TxtSizeValue.Text = size > 0 ? Format.Bytes(size) : "—";
        TxtMeta.Text = Icons.KindOf(_filePath.Length > 0 ? _filePath : item.DisplayName, item.IsTorrent)
                       + (size > 0 ? "  ·  " + Format.Bytes(size) : "");

        if (elapsed is { } t && t.TotalSeconds >= 1)
            TxtDuration.Text = Format.Duration(t)
                + (size > 0 ? $"  ·  ort. {Format.Speed((long)(size / t.TotalSeconds))}" : "");
        else
            PnlDuration.Visibility = Visibility.Hidden;

        if (failed)
        {
            ShowFailed(item);
        }
        else
        {
            Title = "İndirme tamamlandı";
            StartAutoClose();
        }
    }

    private void ShowFailed(DownloadItem item)
    {
        Title = "İndirme başarısız";
        TxtHeader.Text = "İndirme başarısız";
        IcoState.Text = "";

        var danger = (Brush)FindResource("B.Danger");
        IcoState.Foreground = danger;
        Badge.Background = danger;
        IcoBadge.Text = "";

        PnlError.Visibility = Visibility.Visible;
        TxtError.Text = string.IsNullOrWhiteSpace(item.ErrorMessage)
            ? $"aria2 hata kodu {item.ErrorCode}" : item.ErrorMessage!;

        // Yarim kalan dosya acilmaz; klasoru gostermek yine de ise yarar.
        BtnOpenFile.Visibility = Visibility.Collapsed;
        BtnOpenFolder.Visibility = _filePath.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        PnlDuration.Visibility = Visibility.Hidden;
        PnlSize.Visibility = Visibility.Hidden;
    }

    private static long SafeLength(string path)
    {
        try { return path.Length > 0 && File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }

    // ──────────── Kendiliginden kapanma ────────────

    private void StartAutoClose()
    {
        var left = AutoCloseSeconds;
        TxtNote.Text = $"{left} sn sonra kapanır";

        _autoClose = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _autoClose.Tick += (_, _) =>
        {
            if (--left <= 0) { _autoClose!.Stop(); Close(); return; }
            TxtNote.Text = $"{left} sn sonra kapanır";
        };
        _autoClose.Start();

        MouseEnter += (_, _) => CancelAutoClose();
    }

    private void CancelAutoClose()
    {
        if (_autoClose is null) return;
        _autoClose.Stop();
        _autoClose = null;
        TxtNote.Text = "";
    }

    // ──────────── Etkilesim ────────────

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        PostActions.OpenFolder(_filePath);
        Close();
    }

    private void BtnOpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (_filePath.Length == 0 || !File.Exists(_filePath))
        {
            PostActions.OpenFolder(_filePath);
            Close();
            return;
        }

        try
        {
            // UseShellExecute: dosyayi kendi varsayilan programiyla acar. Yonetici
            // yetkisi verilmez; o ayri ve acikca secilen bir islem.
            Process.Start(new ProcessStartInfo(_filePath) { UseShellExecute = true });
            Close();
        }
        catch (Exception ex)
        {
            Log.Warn("Dosya acilamadi: " + ex.Message);
            MessageBox.Show(this, "Dosya açılamadı: " + ex.Message, "Akıntı",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
