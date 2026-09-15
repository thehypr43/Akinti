using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Akinti.Ui;

public partial class SettingsWindow : Window
{
    private const string TaskName = "Akıntı Motoru";
    private readonly Config _cfg;

    public SettingsWindow(Config cfg)
    {
        InitializeComponent();
        _cfg = cfg;
        LoadValues();
    }

    /// <summary>Segmentli secim: 1/2/3/4/8. Baska bir deger varsa en yakini isaretlenir.</summary>
    private RadioButton SegFor(int value) => value switch
    {
        1 => Seg1, 2 => Seg2, 3 => Seg3, 4 => Seg4, _ => Seg8,
    };

    private int SegValue() =>
        Seg1.IsChecked == true ? 1
        : Seg2.IsChecked == true ? 2
        : Seg3.IsChecked == true ? 3
        : Seg4.IsChecked == true ? 4
        : 8;

    private void TxtGlobalLimit_TextChanged(object sender, TextChangedEventArgs e) => UpdateCapLabel();

    private void UpdateCapLabel() =>
        LblCap.Text = "Toplam limit — " +
            (int.TryParse(TxtGlobalLimit.Text.Trim(), out var kb) && kb > 0
                ? Format.Speed((long)kb * 1024)
                : "Sınırsız");

    /// <summary>Tema secimi kaydedilmeyi beklemeden uygulanir: kullanici farki
    /// aninda gorur. Kaydetmeden cikarsa eski secim geri yuklenir.</summary>
    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded && !_loaded) return;
        ThemeManager.Preference = ThemeValue();
        ThemeManager.Apply();
    }

    private string ThemeValue() =>
        ThemeLight.IsChecked == true ? "light"
        : ThemeDark.IsChecked == true ? "dark"
        : "system";

    private bool _loaded;

    private void LoadValues()
    {
        TxtDownloadDir.Text = _cfg.DownloadDir;

        (_cfg.Theme switch
        {
            "light" => ThemeLight,
            "dark" => ThemeDark,
            _ => ThemeSystem,
        }).IsChecked = true;
        _loaded = true;

        SegFor(_cfg.MaxConcurrentDownloads).IsChecked = true;
        TxtConnections.Text = _cfg.ConnectionsPerDownload.ToString();
        TxtGlobalLimit.Text = (_cfg.GlobalSpeedLimitBytes / 1024).ToString();
        UpdateCapLabel();

        var found = Archivers.DetectAll();
        foreach (var a in found) CmbArchiver.Items.Add(a.Path);
        CmbArchiver.Text = _cfg.ArchiverPath;
        TxtArchiverFound.Text = found.Count > 0
            ? "Bulunan: " + string.Join(", ", found.Select(a => a.DisplayName).Distinct())
            : "Kurulu arsivci bulunamadi. 7-Zip, NanaZip veya WinRAR kur.";


        ChkOpenFolderDefault.IsChecked = _cfg.OpenFolderAfterDownloadDefault;
        ChkAutoStart.IsChecked = IsScheduledTaskEnabled();

        TxtExtraArgs.Text = string.Join(Environment.NewLine, _cfg.ExtraAria2Args);

        UpdateEngineState();

        TxtSecurity.Text = _cfg.IsConfigured
            ? $"RPC portu {_cfg.RpcPort}, sadece 127.0.0.1 dinleniyor ve rastgele bir parola ile kilitli. " +
              "Bir web sayfasi bu porta parolasiz istek atarsa aria2 reddeder. " +
              "Dogrulamak icin tarayici konsolundan bu porta istek atmayi deneyebilirsin."
            : "Kurulum tamamlanmamis: scripts\\install.ps1 calistirilmali.";
    }

    // ---------------- Kaydetme ----------------

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var maxConc = SegValue();
        if (!TryParseInt(TxtConnections.Text, 1, 16, out var conns,
                         "Bağlantı sayısı 1-16 arasında olmalı.")) return;
        if (!TryParseInt(TxtGlobalLimit.Text, 0, int.MaxValue, out var limitKb,
                         "Toplam hız limiti negatif olamaz.")) return;
        var dir = TxtDownloadDir.Text.Trim();
        if (string.IsNullOrWhiteSpace(dir)) { Warn("Varsayilan indirme klasoru bos olamaz."); return; }
        try { Directory.CreateDirectory(dir); }
        catch (Exception ex) { Warn("Indirme klasoru olusturulamadi: " + ex.Message); return; }

        _cfg.DownloadDir = dir;
        _cfg.MaxConcurrentDownloads = maxConc;
        _cfg.ConnectionsPerDownload = conns;
        _cfg.GlobalSpeedLimitBytes = (long)limitKb * 1024;
        _cfg.ArchiverPath = CmbArchiver.Text.Trim();
        _cfg.OpenFolderAfterDownloadDefault = ChkOpenFolderDefault.IsChecked == true;
        _cfg.Theme = ThemeValue();
        _cfg.ExtraAria2Args = TxtExtraArgs.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();

        _cfg.Save();
        Aria2Engine.WriteConfFile(_cfg);
        SetScheduledTaskEnabled(ChkAutoStart.IsChecked == true);

        // Aninda uygulanabilenleri canli motora gecir; gerisi motor yeniden baslayinca gecerli olur.
        _ = ApplyLiveAsync();

        DialogResult = true;
        Close();
    }

    private async Task ApplyLiveAsync()
    {
        if (!_cfg.IsConfigured) return;
        try
        {
            using var client = new Aria2Client(_cfg);
            await client.ChangeGlobalOptionAsync(new Dictionary<string, string>
            {
                ["dir"] = _cfg.DownloadDir,
                ["max-concurrent-downloads"] = _cfg.MaxConcurrentDownloads.ToString(),
                ["max-overall-download-limit"] = _cfg.GlobalSpeedLimitBytes.ToString(),
            });
        }
        catch (Exception ex)
        {
            Log.Warn("Canli ayar uygulanamadi: " + ex.Message);
        }
    }

    /// <summary>Kaydetmeden cikildiginda canli denenen tema geri alinir.</summary>
    private void RevertTheme()
    {
        if (ThemeManager.Preference == _cfg.Theme) return;
        ThemeManager.Preference = _cfg.Theme;
        ThemeManager.Apply();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) { RevertTheme(); DialogResult = false; Close(); }

    private async void BtnRestartEngine_Click(object sender, RoutedEventArgs e)
    {
        TxtStatus.Text = "Motor yeniden baslatiliyor...";
        try
        {
            if (_cfg.IsConfigured)
            {
                using var client = new Aria2Client(_cfg);
                try { await client.ShutdownAsync(); } catch { /* zaten kapali olabilir */ }
            }

            for (var i = 0; i < 20 && Aria2Engine.IsProcessRunning(); i++)
                await Task.Delay(250);

            Aria2Engine.WriteConfFile(_cfg);
            TxtStatus.Text = await Aria2Engine.EnsureRunningAsync(_cfg)
                ? "Motor calisiyor."
                : "Motor baslatilamadi, akinti.log dosyasina bak.";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = "Yeniden baslatilamadi: " + ex.Message;
        }
    }

    // ---------------- Gozat ----------------

    private void BtnBrowseDownloadDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Varsayilan indirme klasoru" };
        if (Directory.Exists(TxtDownloadDir.Text)) dlg.InitialDirectory = TxtDownloadDir.Text;
        if (dlg.ShowDialog(this) == true) TxtDownloadDir.Text = dlg.FolderName;
    }

    private void BtnBrowseArchiver_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Arsivci secimi",
            Filter = "Programlar (*.exe)|*.exe",
        };
        if (dlg.ShowDialog(this) == true) CmbArchiver.Text = dlg.FileName;
    }

    // ---------------- Zamanlanmis gorev ----------------

    private static bool IsScheduledTaskEnabled()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("/Query");
            psi.ArgumentList.Add("/TN");
            psi.ArgumentList.Add(TaskName);

            using var p = Process.Start(psi);
            if (p is null) return false;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            if (p.ExitCode != 0) return false;
            // "Devre Disi" / "Disabled" gecmiyorsa etkin sayilir.
            return !output.Contains("Disabled", StringComparison.OrdinalIgnoreCase)
                   && !output.Contains("Devre", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private void SetScheduledTaskEnabled(bool enabled)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("/Change");
            psi.ArgumentList.Add("/TN");
            psi.ArgumentList.Add(TaskName);
            psi.ArgumentList.Add(enabled ? "/ENABLE" : "/DISABLE");

            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            Log.Warn("Zamanlanmis gorev degistirilemedi: " + ex.Message);
        }
    }


    private bool TryParseInt(string text, int min, int max, out int value, string error)
    {
        if (int.TryParse(text.Trim(), out value) && value >= min && value <= max) return true;
        Warn(error);
        return false;
    }

    // ---------------- Motor ----------------

    /// <summary>Motorun su anki hali. Kullanici Gorev Yoneticisi'ne bakmadan
    /// gorsun diye ayarlarda duruyor.</summary>
    private void UpdateEngineState()
    {
        var running = Aria2Engine.IsProcessRunning();
        TxtEngineState.Text = running ? "Çalışıyor" : "Durdu";
        TxtEngineState.Foreground = (System.Windows.Media.Brush)
            FindResource(running ? "B.Success" : "B.TextFaint");
        BtnEngine.Content = running ? "Motoru durdur" : "Motoru başlat";
    }

    private async void BtnEngine_Click(object sender, RoutedEventArgs e)
    {
        BtnEngine.IsEnabled = false;
        try
        {
            if (Aria2Engine.IsProcessRunning())
            {
                TxtStatus.Text = "Motor durduruluyor…";
                var ok = await Aria2Engine.StopAsync(_cfg);
                TxtStatus.Text = ok ? "Motor durduruldu." : "Motor durdurulamadı.";
            }
            else
            {
                TxtStatus.Text = "Motor başlatılıyor…";
                var ok = await Aria2Engine.EnsureRunningAsync(_cfg);
                TxtStatus.Text = ok ? "Motor çalışıyor." : "Motor başlatılamadı.";
            }
        }
        finally
        {
            UpdateEngineState();
            BtnEngine.IsEnabled = true;
        }
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "Akıntı", MessageBoxButton.OK, MessageBoxImage.Warning);
}
