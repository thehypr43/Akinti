using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Akinti.Ui;

/// <summary>
/// Her indirmenin goz onunde onaylandigi yer. Eklentiden gelen mesaj sadece adresi
/// belirler; hedef klasor, arsiv cikarma ve "yonetici olarak calistir" YALNIZCA burada,
/// kullanicinin kendi eliyle secilir.
/// </summary>
public partial class NewDownloadWindow : Window
{
    private static readonly string[] RunnableExtensions = [".exe", ".msi", ".msix", ".bat", ".cmd", ".ps1"];

    private readonly Config _cfg = Config.Load();
    private readonly string? _referrer;
    private CancellationTokenSource? _probeCts;
    private bool _folderTouchedByUser;
    private bool _userEditedFileName;
    private bool _userTouchedExtract;
    private bool _loading = true;

    public NewDownloadWindow(string? url, string? referrer = null)
    {
        InitializeComponent();
        _referrer = referrer;

        for (var i = 1; i <= 16; i++) CmbConnections.Items.Add(i);
        CmbConnections.SelectedIndex = Math.Clamp(_cfg.ConnectionsPerDownload, 1, 16) - 1;

        foreach (var f in _cfg.RecentFolders) CmbFolder.Items.Add(f);
        if (!CmbFolder.Items.Contains(_cfg.DownloadDir)) CmbFolder.Items.Add(_cfg.DownloadDir);
        CmbFolder.Text = _cfg.DownloadDir;

        ChkOpenFolder.IsChecked = _cfg.OpenFolderAfterDownloadDefault;
        TxtSeedRatio.Text = _cfg.SeedRatio.ToString(CultureInfo.InvariantCulture);

        CmbFolder.AddHandler(TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((_, _) => { if (!_loading) _folderTouchedByUser = true; }));

        TxtUrl.Text = url ?? "";
        TxtDialogTitle.Text = url is null ? "Yeni indirme" : "Bağlantı yakalandı";
        Loaded += OnLoaded;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = false;

        if (string.IsNullOrWhiteSpace(TxtUrl.Text))
        {
            TxtUrl.Focus();
            TxtSize.Text = "Bir adres yapıştırın";
            TxtFileTitle.Text = "—";
            UpdateOptionAvailability();
            return;
        }

        await ProbeAsync();
    }

    // ──────────── Kaynak yoklama ────────────

    private async Task ProbeAsync()
    {
        var url = TxtUrl.Text.Trim();
        if (!UrlHelper.IsSupportedScheme(url))
        {
            TxtSize.Text = "";
            TxtStatus.Text = string.IsNullOrWhiteSpace(url) ? "" : "Desteklenmeyen adres.";
            UpdateOptionAvailability();
            return;
        }

        TxtStatus.Text = "";
        UpdateTorrentPanel(url);

        _probeCts?.Cancel();
        _probeCts = new CancellationTokenSource();
        var ct = _probeCts.Token;

        var host = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "";

        if (UrlHelper.IsMagnet(url))
        {
            SetFileName(UrlHelper.GuessFileName(url));
            TxtSize.Text = "Boyut torrent bilgileri gelince belli olur";
            UpdateOptionAvailability();
            return;
        }

        TxtSize.Text = "Boyut sorgulanıyor…";
        SetFileName(UrlHelper.GuessFileName(url));

        var info = await RemoteProbe.ProbeAsync(url, ct);
        if (ct.IsCancellationRequested) return;

        SetFileName(info.FileName);
        TxtSize.Text = info.Size > 0
            ? Format.Bytes(info.Size) + (host.Length > 0 ? "  ·  " + host : "")
            : (host.Length > 0 ? "Boyut bilinmiyor  ·  " + host : "Boyut bilinmiyor");

        if (info.Size > 0 && !info.Resumable)
            TxtStatus.Text = "Sunucu parçalı indirmeyi desteklemiyor, tek bağlantı kullanılacak.";

        UpdateOptionAvailability();
    }

    private void SetFileName(string name)
    {
        if (!_userEditedFileName)
        {
            var suppress = _loading;
            _loading = true;
            TxtFileName.Text = name;
            _loading = suppress;
        }
        TxtFileTitle.Text = string.IsNullOrWhiteSpace(TxtFileName.Text) ? "—" : TxtFileName.Text;
        IcoFile.Text = Icons.ForFile(TxtFileName.Text, PnlTorrent.Visibility == Visibility.Visible);
        ApplyFolderRule();
    }

    private void TxtFileName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading) _userEditedFileName = true;
        TxtFileTitle.Text = string.IsNullOrWhiteSpace(TxtFileName.Text) ? "—" : TxtFileName.Text;
        IcoFile.Text = Icons.ForFile(TxtFileName.Text, PnlTorrent.Visibility == Visibility.Visible);
        ApplyFolderRule();
        UpdateOptionAvailability();
    }

    private async void TxtUrl_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        _userEditedFileName = false;
        await ProbeAsync();
    }

    /// <summary>Uzantiya gore klasor onerisi. Kullanici klasoru elle degistirdiyse dokunmaz.</summary>
    private void ApplyFolderRule()
    {
        var name = TxtFileName.Text.Trim();
        if (string.IsNullOrEmpty(name)) { TxtRuleHint.Text = ""; return; }

        var folder = _cfg.ResolveFolderFor(name);
        var ext = Path.GetExtension(name);

        TxtRuleHint.Text = folder != _cfg.DownloadDir
            ? $"{ext} kuralı bu dosyayı \"{Path.GetFileName(folder)}\" klasörüne yönlendiriyor."
            : "";

        if (!_folderTouchedByUser)
        {
            var suppress = _loading;
            _loading = true;
            CmbFolder.Text = folder;
            _loading = suppress;
        }
    }

    private void UpdateTorrentPanel(string url) =>
        PnlTorrent.Visibility = UrlHelper.IsMagnet(url) || UrlHelper.IsTorrentFileUrl(url)
            ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Uygulanamayacak secenekler pasif kalir; sessizce yok sayilan kutu olmaz.</summary>
    private void UpdateOptionAvailability()
    {
        var name = TxtFileName.Text.Trim();
        var ext = Path.GetExtension(name);
        var isTorrentSource = PnlTorrent.Visibility == Visibility.Visible;

        var archiverExists = Archivers.AnyAvailable();
        var canExtract = !isTorrentSource && Archivers.IsArchive(name) && archiverExists;
        ChkExtract.IsEnabled = canExtract;
        if (!canExtract) ChkExtract.IsChecked = false;

        if (!isTorrentSource && Archivers.IsArchive(name) && !archiverExists)
        {
            TxtArchiverHint.Text = "Kurulu bir arşivci bulunamadı. 7-Zip, NanaZip veya WinRAR kurun.";
            TxtArchiverHint.Visibility = Visibility.Visible;
        }
        else TxtArchiverHint.Visibility = Visibility.Collapsed;

        var canRun = !isTorrentSource && RunnableExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        ChkRunAdmin.IsEnabled = canRun;
        if (!canRun) ChkRunAdmin.IsChecked = false;

        if (canExtract && _cfg.ExtractArchivesDefault && !_userTouchedExtract)
            ChkExtract.IsChecked = true;
    }

    private void ChkExtract_Changed(object sender, RoutedEventArgs e)
    {
        _userTouchedExtract = true;
        ChkDeleteArchive.IsEnabled = ChkExtract.IsChecked == true;
        if (ChkExtract.IsChecked != true) ChkDeleteArchive.IsChecked = false;
    }

    private void ChkRunAdmin_Changed(object sender, RoutedEventArgs e) =>
        PnlAdminWarn.Visibility = ChkRunAdmin.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Kayıt klasörü seç",
            InitialDirectory = Directory.Exists(CmbFolder.Text) ? CmbFolder.Text : _cfg.DownloadDir,
        };
        if (dlg.ShowDialog(this) == true)
        {
            _folderTouchedByUser = true;
            CmbFolder.Text = dlg.FolderName;
        }
    }

    // ──────────── Kuyruga ekleme ────────────

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

    private async void BtnDownload_Click(object sender, RoutedEventArgs e) => await StartAsync(paused: false);

    private async void BtnLater_Click(object sender, RoutedEventArgs e) => await StartAsync(paused: true);

    private async Task StartAsync(bool paused)
    {
        var url = TxtUrl.Text.Trim();
        if (!UrlHelper.IsSupportedScheme(url)) { Warn("Geçerli bir indirme adresi girin."); return; }

        var folder = CmbFolder.Text.Trim();
        if (string.IsNullOrWhiteSpace(folder)) { Warn("Bir kayıt klasörü seçin."); return; }

        if (!_cfg.IsConfigured)
        {
            Warn("Kurulum tamamlanmamış. Önce scripts\\install.ps1 dosyasını çalıştırın.");
            return;
        }

        SetBusy(true, "Motor kontrol ediliyor…");

        if (!await Aria2Engine.EnsureRunningAsync(_cfg))
        {
            SetBusy(false, "");
            Warn("İndirme motoru başlatılamadı. Ayrıntı için akinti.log dosyasına bakın.");
            return;
        }

        var req = new DownloadRequest
        {
            Url = url,
            FileName = TxtFileName.Text.Trim(),
            TargetDir = folder,
            Connections = CmbConnections.SelectedIndex >= 0 ? CmbConnections.SelectedIndex + 1 : 8,
            SpeedLimitBytes = ParseSpeedLimitBytes(),
            StartPaused = paused,
            Extract = ChkExtract.IsChecked == true,
            DeleteArchiveAfterExtract = ChkDeleteArchive.IsChecked == true,
            RunAsAdmin = ChkRunAdmin.IsChecked == true,
            OpenFolder = ChkOpenFolder.IsChecked == true,
            Referrer = _referrer,
        };

        if (PnlTorrent.Visibility == Visibility.Visible
            && double.TryParse(TxtSeedRatio.Text.Trim(), NumberStyles.Float,
                               CultureInfo.InvariantCulture, out var ratio))
            req.SeedRatio = ratio;

        try
        {
            SetBusy(true, "Kuyruğa ekleniyor…");
            using var client = new Aria2Client(_cfg);
            var gid = await DownloadService.EnqueueAsync(client, _cfg, req);

            var pickFiles = PnlTorrent.Visibility == Visibility.Visible && ChkPickFiles.IsChecked == true;
            Close();

            if (pickFiles) new TorrentFilesWindow(_cfg, gid).Show();
        }
        catch (Exception ex)
        {
            SetBusy(false, "");
            Log.Error("Kuyruğa eklenemedi", ex);
            Warn("İndirme başlatılamadı: " + ex.Message);
        }
    }

    private long ParseSpeedLimitBytes() =>
        long.TryParse(TxtSpeedLimit.Text.Trim(), out var kb) && kb > 0 ? kb * 1024 : 0;

    private void SetBusy(bool busy, string status)
    {
        BtnDownload.IsEnabled = !busy;
        BtnLater.IsEnabled = !busy;
        TxtStatus.Text = status;
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "Akıntı", MessageBoxButton.OK, MessageBoxImage.Warning);
}
