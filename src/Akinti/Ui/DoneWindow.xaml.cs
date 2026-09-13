using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Akinti.Ui;

/// <summary>
/// Indirme bitince ekranin ortasinda acilan sonuc penceresi. Ilerleme penceresi
/// kendini kapatip yerine bunu acar, boylece "iniyor" ile "bitti" ayni pencerede
/// karisik durmaz.
///
/// Kendiliginden kapanmaz: kullanici kapatana kadar durur.
/// </summary>
public partial class DoneWindow : Window
{
    private readonly string _filePath;
    private readonly bool _isTorrent;
    private string? _extractedDir;

    public DoneWindow(DownloadItem item, TimeSpan? elapsed = null)
    {
        InitializeComponent();
        WindowEffects.Attach(this);

        _filePath = PostActions.ResolveFilePath(item.FilePath, item.Dir, item.DisplayName);
        _isTorrent = item.IsTorrent;
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
        {
            // Sure bilinmiyorsa satirda bosluk kalmasin: boyut sola kaysin.
            PnlDuration.Visibility = Visibility.Collapsed;
            Grid.SetColumn(PnlSize, 0);
        }

        if (failed) ShowFailed(item);
        else ShowCompleted();
    }

    /// <summary>Dugmeler yalnizca dosya turu izin veriyorsa gorunur: arsiv degilse
    /// "Klasore cikar", calistirilabilir degilse "Yonetici ac" hic cizilmez.</summary>
    private void ShowCompleted()
    {
        Title = "İndirme tamamlandı";

        var exists = _filePath.Length > 0 && File.Exists(_filePath);
        BtnExtract.Visibility = exists && !_isTorrent
            && Archivers.IsArchive(_filePath) && Archivers.AnyAvailable()
            ? Visibility.Visible : Visibility.Collapsed;
        BtnRunAdmin.Visibility = exists && !_isTorrent && PostActions.IsRunnable(_filePath)
            ? Visibility.Visible : Visibility.Collapsed;
        BtnOpenFile.Visibility = exists || _filePath.Length > 0
            ? Visibility.Visible : Visibility.Collapsed;
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

        // Yarim kalan dosya ne acilir ne cikarilir; klasoru gostermek yine de ise yarar.
        BtnOpenFile.Visibility = Visibility.Collapsed;
        BtnExtract.Visibility = Visibility.Collapsed;
        BtnRunAdmin.Visibility = Visibility.Collapsed;
        BtnOpenFolder.Visibility = _filePath.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        PnlDuration.Visibility = Visibility.Collapsed;
        PnlSize.Visibility = Visibility.Collapsed;
    }

    private static long SafeLength(string path)
    {
        try { return path.Length > 0 && File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }

    // ──────────── Etkilesim ────────────

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        PostActions.OpenFolder(_extractedDir ?? _filePath);
        Close();
    }

    private async void BtnExtract_Click(object sender, RoutedEventArgs e)
    {
        BtnExtract.IsEnabled = false;
        TxtNote.Text = "Çıkarılıyor…";
        try
        {
            var result = await Archivers.ExtractAsync(_filePath, Config.Load());

            if (result.Success)
            {
                // Isini yapan dugme durmasin; nereye cikardigini "Klasor" gosterir.
                BtnExtract.Visibility = Visibility.Collapsed;
                TxtNote.Text = "Klasöre çıkarıldı";
                if (!string.IsNullOrEmpty(result.TargetDir)) _extractedDir = result.TargetDir;
            }
            else
            {
                TxtNote.Text = result.Message;
                BtnExtract.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Cikarma basarisiz", ex);
            TxtNote.Text = "Çıkarılamadı: " + ex.Message;
            BtnExtract.IsEnabled = true;
        }
    }

    private void BtnRunAdmin_Click(object sender, RoutedEventArgs e)
    {
        // UAC istemini Windows gosterir; kullanici reddederse pencere kapanmaz.
        var message = PostActions.RunAsAdmin(_filePath);
        TxtNote.Text = message;
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
