using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;

namespace HafifDM.Ui;

public partial class SettingsWindow : Window
{
    private const string TaskName = "HafifDM Engine";
    private readonly Config _cfg;

    public SettingsWindow(Config cfg)
    {
        InitializeComponent();
        _cfg = cfg;
        LoadValues();
    }

    private void LoadValues()
    {
        TxtDownloadDir.Text = _cfg.DownloadDir;
        TxtRules.Text = RulesToText(_cfg.TypeRules);

        TxtMaxConcurrent.Text = _cfg.MaxConcurrentDownloads.ToString();
        TxtConnections.Text = _cfg.ConnectionsPerDownload.ToString();
        TxtGlobalLimit.Text = (_cfg.GlobalSpeedLimitBytes / 1024).ToString();

        var found = Archivers.DetectAll();
        foreach (var a in found) CmbArchiver.Items.Add(a.Path);
        CmbArchiver.Text = _cfg.ArchiverPath;
        TxtArchiverFound.Text = found.Count > 0
            ? "Bulunan: " + string.Join(", ", found.Select(a => a.DisplayName).Distinct())
            : "Kurulu arsivci bulunamadi. 7-Zip, NanaZip veya WinRAR kur.";

        TxtTorrentPort.Text = _cfg.TorrentPort.ToString();
        TxtSeedRatio.Text = _cfg.SeedRatio.ToString(CultureInfo.InvariantCulture);
        TxtSeedTime.Text = _cfg.SeedTimeMinutes.ToString();
        ChkDht.IsChecked = _cfg.EnableDht;

        ChkExtractDefault.IsChecked = _cfg.ExtractArchivesDefault;
        ChkOpenFolderDefault.IsChecked = _cfg.OpenFolderAfterDownloadDefault;
        ChkAutoStart.IsChecked = IsScheduledTaskEnabled();

        TxtExtraArgs.Text = string.Join(Environment.NewLine, _cfg.ExtraAria2Args);

        TxtSecurity.Text = _cfg.IsConfigured
            ? $"RPC portu {_cfg.RpcPort}, sadece 127.0.0.1 dinleniyor ve rastgele bir parola ile kilitli. " +
              "Bir web sayfasi bu porta parolasiz istek atarsa aria2 reddeder. " +
              "Dogrulamak icin tarayici konsolundan bu porta istek atmayi deneyebilirsin."
            : "Kurulum tamamlanmamis: scripts\\install.ps1 calistirilmali.";
    }

    // ---------------- Kaydetme ----------------

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseInt(TxtMaxConcurrent.Text, 1, 20, out var maxConc,
                         "Ayni anda indirme sayisi 1-20 arasinda olmali.")) return;
        if (!TryParseInt(TxtConnections.Text, 1, 16, out var conns,
                         "Baglanti sayisi 1-16 arasinda olmali.")) return;
        if (!TryParseInt(TxtGlobalLimit.Text, 0, int.MaxValue, out var limitKb,
                         "Toplam hiz limiti negatif olamaz.")) return;
        if (!TryParseInt(TxtTorrentPort.Text, 1024, 65535, out var btPort,
                         "Torrent portu 1024-65535 arasinda olmali.")) return;
        if (!TryParseInt(TxtSeedTime.Text, 0, int.MaxValue, out var seedTime,
                         "Paylasim suresi negatif olamaz.")) return;

        if (!double.TryParse(TxtSeedRatio.Text.Trim(), NumberStyles.Float,
                             CultureInfo.InvariantCulture, out var ratio) || ratio < 0)
        {
            Warn("Paylasim orani gecerli bir sayi olmali (ornek: 1.0).");
            return;
        }

        var dir = TxtDownloadDir.Text.Trim();
        if (string.IsNullOrWhiteSpace(dir)) { Warn("Varsayilan indirme klasoru bos olamaz."); return; }
        try { Directory.CreateDirectory(dir); }
        catch (Exception ex) { Warn("Indirme klasoru olusturulamadi: " + ex.Message); return; }

        _cfg.DownloadDir = dir;
        _cfg.TypeRules = TextToRules(TxtRules.Text);
        _cfg.MaxConcurrentDownloads = maxConc;
        _cfg.ConnectionsPerDownload = conns;
        _cfg.GlobalSpeedLimitBytes = (long)limitKb * 1024;
        _cfg.ArchiverPath = CmbArchiver.Text.Trim();
        _cfg.TorrentPort = btPort;
        _cfg.SeedRatio = ratio;
        _cfg.SeedTimeMinutes = seedTime;
        _cfg.EnableDht = ChkDht.IsChecked == true;
        _cfg.ExtractArchivesDefault = ChkExtractDefault.IsChecked == true;
        _cfg.OpenFolderAfterDownloadDefault = ChkOpenFolderDefault.IsChecked == true;
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

    private void BtnCancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

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
                : "Motor baslatilamadi, hafifdm.log dosyasina bak.";
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

    // ---------------- Kural metni ----------------

    private static string RulesToText(Dictionary<string, string> rules)
    {
        var sb = new StringBuilder();
        foreach (var group in rules.GroupBy(r => r.Value, StringComparer.OrdinalIgnoreCase)
                                   .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"{string.Join(",", group.Select(g => g.Key))} = {group.Key}");
        return sb.ToString().TrimEnd();
    }

    private static Dictionary<string, string> TextToRules(string text)
    {
        var rules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var idx = line.IndexOf('=');
            if (idx <= 0) continue;

            var folder = line[(idx + 1)..].Trim();
            if (folder.Length == 0) continue;

            foreach (var extRaw in line[..idx].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var ext = extRaw.Trim();
                if (ext.Length == 0) continue;
                if (!ext.StartsWith('.')) ext = "." + ext;
                rules[ext] = folder;
            }
        }
        return rules;
    }

    private bool TryParseInt(string text, int min, int max, out int value, string error)
    {
        if (int.TryParse(text.Trim(), out value) && value >= min && value <= max) return true;
        Warn(error);
        return false;
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "HafifDM", MessageBoxButton.OK, MessageBoxImage.Warning);
}
