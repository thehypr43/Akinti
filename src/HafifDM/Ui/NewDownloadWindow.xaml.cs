using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace HafifDM.Ui;

/// <summary>
/// Her indirmenin goz onunde onaylandigi yer. Eklentiden gelen mesaj sadece adresi
/// belirler; hedef klasor, arsiv cikarma ve "yonetici olarak calistir" YALNIZCA burada,
/// kullanicinin kendi eliyle secilir.
/// </summary>
public partial class NewDownloadWindow : Window
{
    private static readonly string[] RunnableExtensions = [".exe", ".msi", ".msix", ".bat", ".cmd", ".ps1"];

    private readonly Config _cfg = Config.Load();
    private bool _folderTouchedByUser;
    private bool _loading = true;

    public NewDownloadWindow(string? url)
    {
        InitializeComponent();

        for (var i = 1; i <= 16; i++) CmbConnections.Items.Add(i);
        // SelectedItem ile kutulanmis int eslesmesine guvenme; indeks kesin.
        CmbConnections.SelectedIndex = Math.Clamp(_cfg.ConnectionsPerDownload, 1, 16) - 1;

        foreach (var f in _cfg.RecentFolders) CmbFolder.Items.Add(f);
        if (!CmbFolder.Items.Contains(_cfg.DownloadDir)) CmbFolder.Items.Add(_cfg.DownloadDir);
        CmbFolder.Text = _cfg.DownloadDir;

        ChkOpenFolder.IsChecked = _cfg.OpenFolderAfterDownloadDefault;
        TxtSeedRatio.Text = _cfg.SeedRatio.ToString(CultureInfo.InvariantCulture);

        CmbFolder.AddHandler(TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((_, _) => { if (!_loading) _folderTouchedByUser = true; }));

        TxtUrl.Text = url ?? "";
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = false;

        if (string.IsNullOrWhiteSpace(TxtUrl.Text))
        {
            TxtUrl.Focus();
            TxtSize.Text = "";
            UpdateOptionAvailability();
            return;
        }

        await ProbeAsync();
    }

    // ---------------- Kaynak yoklama ----------------

    private CancellationTokenSource? _probeCts;

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

        if (UrlHelper.IsMagnet(url))
        {
            SetFileName(UrlHelper.GuessFileName(url));
            TxtSize.Text = "Boyut torrent bilgileri gelince belli olur";
            UpdateOptionAvailability();
            return;
        }

        TxtSize.Text = "Boyut sorgulaniyor...";
        SetFileName(UrlHelper.GuessFileName(url));

        var info = await RemoteProbe.ProbeAsync(url, ct);
        if (ct.IsCancellationRequested) return;

        SetFileName(info.FileName);
        TxtSize.Text = info.Size > 0 ? Format.Bytes(info.Size) : "Boyut bilinmiyor";
        if (info.Size > 0 && !info.Resumable)
            TxtStatus.Text = "Sunucu parcali indirmeyi desteklemiyor, tek baglanti kullanilacak.";

        UpdateOptionAvailability();
    }

    private void SetFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(TxtFileName.Text) || !_userEditedFileName)
        {
            var suppress = _loading;
            _loading = true;
            TxtFileName.Text = name;
            _loading = suppress;
        }
        ApplyFolderRule();
    }

    private bool _userEditedFileName;

    private void TxtFileName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading) _userEditedFileName = true;
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
        var ext = System.IO.Path.GetExtension(name);

        TxtRuleHint.Text = folder != _cfg.DownloadDir
            ? $"{ext} kurali bu dosyayi \"{System.IO.Path.GetFileName(folder)}\" klasorune yonlendiriyor."
            : "";

        if (!_folderTouchedByUser)
        {
            var suppress = _loading;
            _loading = true;
            CmbFolder.Text = folder;
            _loading = suppress;
        }
    }

    private void UpdateTorrentPanel(string url)
    {
        var isTorrent = UrlHelper.IsMagnet(url) || UrlHelper.IsTorrentFileUrl(url);
        PnlTorrent.Visibility = isTorrent ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Uygulanamayacak secenekler pasif kalir; sessizce yok sayilan kutu olmaz.</summary>
    private void UpdateOptionAvailability()
    {
        var name = TxtFileName.Text.Trim();
        var ext = System.IO.Path.GetExtension(name);
        var isTorrentSource = PnlTorrent.Visibility == Visibility.Visible;

        var archiverExists = Archivers.AnyAvailable();
        var canExtract = !isTorrentSource && Archivers.IsArchive(name) && archiverExists;
        ChkExtract.IsEnabled = canExtract;
        if (!canExtract) ChkExtract.IsChecked = false;

        if (!isTorrentSource && Archivers.IsArchive(name) && !archiverExists)
        {
            TxtArchiverHint.Text = "Kurulu bir arsivci bulunamadi. 7-Zip, NanaZip veya WinRAR kur.";
            TxtArchiverHint.Visibility = Visibility.Visible;
        }
        else TxtArchiverHint.Visibility = Visibility.Collapsed;

        var canRun = !isTorrentSource
                     && RunnableExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        ChkRunAdmin.IsEnabled = canRun;
        if (!canRun) ChkRunAdmin.IsChecked = false;

        if (canExtract && _cfg.ExtractArchivesDefault && !_userTouchedExtract)
            ChkExtract.IsChecked = true;
    }

    private bool _userTouchedExtract;

    private void ChkExtract_Changed(object sender, RoutedEventArgs e)
    {
        _userTouchedExtract = true;
        ChkDeleteArchive.IsEnabled = ChkExtract.IsChecked == true;
        if (ChkExtract.IsChecked != true) ChkDeleteArchive.IsChecked = false;
    }

    private void ChkRunAdmin_Changed(object sender, RoutedEventArgs e) =>
        TxtAdminWarn.Visibility = ChkRunAdmin.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Kayit klasoru sec",
            InitialDirectory = System.IO.Directory.Exists(CmbFolder.Text) ? CmbFolder.Text : _cfg.DownloadDir,
        };
        if (dlg.ShowDialog(this) == true)
        {
            _folderTouchedByUser = true;
            CmbFolder.Text = dlg.FolderName;
        }
    }

    // ---------------- Kuyruga ekleme ----------------

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

    private async void BtnDownload_Click(object sender, RoutedEventArgs e) => await StartAsync(paused: false);

    private async void BtnLater_Click(object sender, RoutedEventArgs e) => await StartAsync(paused: true);

    private async Task StartAsync(bool paused)
    {
        var url = TxtUrl.Text.Trim();
        if (!UrlHelper.IsSupportedScheme(url)) { Warn("Gecerli bir indirme adresi gir."); return; }

        var folder = CmbFolder.Text.Trim();
        if (string.IsNullOrWhiteSpace(folder)) { Warn("Bir kayit klasoru sec."); return; }

        if (!_cfg.IsConfigured)
        {
            Warn("Kurulum tamamlanmamis. Once scripts\\install.ps1 dosyasini calistir.");
            return;
        }

        SetBusy(true, "Motor kontrol ediliyor...");

        if (!await Aria2Engine.EnsureRunningAsync(_cfg))
        {
            SetBusy(false, "");
            Warn("Indirme motoru baslatilamadi. Ayrinti icin hafifdm.log dosyasina bak.");
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
        };

        if (PnlTorrent.Visibility == Visibility.Visible
            && double.TryParse(TxtSeedRatio.Text.Trim(), NumberStyles.Float,
                               CultureInfo.InvariantCulture, out var ratio))
            req.SeedRatio = ratio;

        try
        {
            SetBusy(true, "Kuyruga ekleniyor...");
            using var client = new Aria2Client(_cfg);
            var gid = await DownloadService.EnqueueAsync(client, _cfg, req);

            var pickFiles = PnlTorrent.Visibility == Visibility.Visible && ChkPickFiles.IsChecked == true;
            Close();

            if (pickFiles) new TorrentFilesWindow(_cfg, gid).Show();
        }
        catch (Exception ex)
        {
            SetBusy(false, "");
            Log.Error("Kuyruga eklenemedi", ex);
            Warn("Indirme baslatilamadi: " + ex.Message);
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
        MessageBox.Show(this, message, "HafifDM", MessageBoxButton.OK, MessageBoxImage.Warning);
}
