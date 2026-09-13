using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Akinti.Ui;

/// <summary>
/// Indirme bitince ekranin ortasinda acilan sonuc penceresi. Ilerleme penceresi
/// (MiniWindow) kendini kapatip yerine bunu acar, boylece "iniyor" ile "bitti"
/// ayni pencerede karisik durmaz.
///
/// Basarili sonuc bir sure sonra kendiliginden kapanir; hata kalir, cunku okunmasi
/// gereken durum odur. Fare pencereye girdiginde sayac iptal edilir: kullanici
/// okurken pencere altindan kaybolmasin.
/// </summary>
public partial class DoneWindow : Window
{
    private const int AutoCloseSeconds = 30;

    private readonly string _filePath;
    private DispatcherTimer? _autoClose;

    public DoneWindow(DownloadItem item)
    {
        InitializeComponent();

        _filePath = item.FilePath ?? "";
        var failed = item.Status == Aria2Status.Error;

        IcoFile.Text = Icons.ForFile(_filePath.Length > 0 ? _filePath : item.DisplayName, item.IsTorrent);
        TxtName.Text = item.DisplayName;

        var host = string.IsNullOrEmpty(item.SourceUri) ? ""
            : Uri.TryCreate(item.SourceUri, UriKind.Absolute, out var u) ? u.Host : "";

        if (failed)
        {
            Title = "İndirme başarısız";
            TxtHeader.Text = "İndirme başarısız";
            IcoState.Text = "";
            IcoState.Foreground = (System.Windows.Media.Brush)FindResource("B.Danger");
            IcoFile.Foreground = (System.Windows.Media.Brush)FindResource("B.Danger");

            TxtMeta.Text = host.Length > 0 ? host : "";
            TxtDetail.Text = string.IsNullOrWhiteSpace(item.ErrorMessage)
                ? $"aria2 hata kodu {item.ErrorCode}" : item.ErrorMessage!;
            TxtDetail.Foreground = (System.Windows.Media.Brush)FindResource("B.Danger");

            BtnOpenFile.Visibility = Visibility.Collapsed;
            BtnOpenFolder.Visibility = _filePath.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        Title = "İndirme tamamlandı";
        TxtHeader.Text = "İndirme tamamlandı";
        TxtMeta.Text = Format.Bytes(item.TotalLength) + (host.Length > 0 ? "  ·  " + host : "");
        TxtDetail.Text = _filePath.Length > 0 ? Path.GetDirectoryName(_filePath) ?? "" : "";

        BtnOpenFile.Visibility = _filePath.Length > 0 && File.Exists(_filePath)
            ? Visibility.Visible : Visibility.Collapsed;
        BtnOpenFolder.Visibility = _filePath.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        StartAutoClose();
    }

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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_filePath.Length > 0) PostActions.OpenFolder(_filePath);
        Close();
    }

    private void BtnOpenFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // UseShellExecute: dosyayi kendi varsayilan programiyla acar. Yonetici
            // yetkisi verilmez; "yonetici olarak calistir" yalnizca indirme
            // diyalogunda isaretlenen ayri bir yol.
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
