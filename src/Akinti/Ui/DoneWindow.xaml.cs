using System.Collections.Generic;
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
    private string? _extractedDir;

    public DoneWindow(DownloadItem item, TimeSpan? elapsed = null)
    {
        InitializeComponent();
        WindowEffects.Attach(this);

        _filePath = PostActions.ResolveFilePath(item.FilePath, item.Dir, item.DisplayName);
        var failed = item.Status == Aria2Status.Error;
        var shown = _filePath.Length > 0 ? _filePath : item.DisplayName;

        TxtName.Text = item.DisplayName;
        TxtKind.Text = KindShort(shown);

        var size = item.TotalLength > 0 ? item.TotalLength : SafeLength(_filePath);
        var meta = new List<string>();
        if (size > 0) meta.Add(Format.Bytes(size));
        if (elapsed is { } t && t.TotalSeconds >= 1)
        {
            meta.Add(Format.Duration(t));
            if (size > 0) meta.Add("ortalama " + Format.Speed((long)(size / t.TotalSeconds)));
        }
        TxtMeta.Text = string.Join("  ·  ", meta);

        if (failed) ShowFailed(item);
        else ShowCompleted();
    }

    /// <summary>Dosya karesindeki uzanti yazisi (tasarimda ikon degil, uzanti).</summary>
    private static string KindShort(string nameOrPath)
    {
        var ext = Path.GetExtension(nameOrPath).TrimStart('.');
        if (ext.Length == 0) return "DSY";
        return (ext.Length > 4 ? ext[..4] : ext).ToUpperInvariant();
    }

    /// <summary>Dugmeler yalnizca dosya turu izin veriyorsa gorunur: arsiv degilse
    /// "Klasore cikar", calistirilabilir degilse "Yonetici olarak ac" hic cizilmez.</summary>
    private void ShowCompleted()
    {
        Title = "İndirme tamamlandı";
        TxtHeading.Text = "İndirme tamamlandı";

        var folder = _filePath.Length > 0 ? Path.GetFileName(Path.GetDirectoryName(_filePath) ?? "") : "";
        TxtSub.Text = folder.Length > 0
            ? $"Dosya {folder} klasörüne kaydedildi."
            : "Dosya kaydedildi.";

        var exists = _filePath.Length > 0 && File.Exists(_filePath);
        BtnExtract.Visibility = exists && Archivers.IsArchive(_filePath) && Archivers.AnyAvailable()
            ? Visibility.Visible : Visibility.Collapsed;
        BtnRunAdmin.Visibility = exists && PostActions.IsRunnable(_filePath)
            ? Visibility.Visible : Visibility.Collapsed;
        var extract = BtnExtract.Visibility == Visibility.Visible;
        var admin = BtnRunAdmin.Visibility == Visibility.Visible;
        PnlExtra.Visibility = extract || admin ? Visibility.Visible : Visibility.Collapsed;

        // Tek dugme kaldiysa yarim satirda asili durmasin, tam genisligi alsin.
        if (extract && !admin) { Grid.SetColumnSpan(BtnExtract, 2); BtnExtract.Margin = new Thickness(0); }
        if (admin && !extract) { Grid.SetColumn(BtnRunAdmin, 0); Grid.SetColumnSpan(BtnRunAdmin, 2); }
        BtnOpenFile.Visibility = exists ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowFailed(DownloadItem item)
    {
        Title = "İndirme başarısız";
        TxtHeading.Text = "İndirme başarısız";
        TxtSub.Text = "Dosya alınamadı.";

        var danger = (Brush)FindResource("B.Danger");
        Badge.Background = danger;
        IcoBadge.Text = "\uE711";

        PnlError.Visibility = Visibility.Visible;
        TxtError.Text = string.IsNullOrWhiteSpace(item.ErrorMessage)
            ? $"aria2 hata kodu {item.ErrorCode}" : item.ErrorMessage!;

        // Yarim kalan dosya ne acilir ne cikarilir; klasoru gostermek yine de ise yarar.
        BtnOpenFile.Visibility = Visibility.Collapsed;
        PnlExtra.Visibility = Visibility.Collapsed;
        BtnOpenFolder.Visibility = _filePath.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static long SafeLength(string path)
    {
        try { return path.Length > 0 && File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }

    // ──────────── Etkilesim ────────────

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
            var result = await ExtractFlow.RunAsync(_filePath, Config.Load(), this);

            if (result.Success)
            {
                // Isini yapan dugme durmasin; nereye cikardigini "Klasorde goster" gosterir.
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
