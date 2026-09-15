using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Akinti.Ui;

/// <summary>
/// Her indirmenin goz onunde onaylandigi yer. Eklentiden gelen mesaj sadece adresi
/// belirler; hedef klasor YALNIZCA burada, kullanicinin kendi eliyle degistirilir.
///
/// Pencere bilerek kucuk: dosya, nereye inecegi, iki dugme. Burada indirme
/// sonrasina dair HICBIR secenek yok - ne arsiv cikarma ne yonetici olarak
/// calistirma. Ikisi de daha eline gecmemis bir dosya hakkinda pesinen karar
/// vermek demek; ikisi de dosya inip goruldukten sonra sonuc penceresinde,
/// dugme olarak duruyor.
/// </summary>
public partial class NewDownloadWindow : Window
{
    private readonly Config _cfg = Config.Load();
    private readonly string? _referrer;
    private readonly bool _hasSuggestedName;
    private string? _cookie;
    private string? _userAgent;

    private CancellationTokenSource? _probeCts;
    private string _folder;
    private string _fileName = "";
    private bool _userEditedFileName;
    private bool _loading = true;

    /// <summary>Tarayicidan devralinan indirme: adres, ad, cerez ve kimlik dizesi hazir gelir.</summary>
    public NewDownloadWindow(CapturedRequest captured)
        : this(captured.Url, captured.Referrer, captured.FileName)
    {
        _cookie = captured.Cookie;
        _userAgent = captured.UserAgent;
    }

    public NewDownloadWindow(string? url, string? referrer = null, string? suggestedName = null)
    {
        InitializeComponent();
        WindowEffects.Attach(this);

        _referrer = referrer;
        _hasSuggestedName = !string.IsNullOrWhiteSpace(suggestedName);

        _folder = _cfg.DownloadDir;
        TxtFolder.Text = _folder;

        TxtUrl.Text = url ?? "";
        Title = url is null ? "Yeni indirme" : "Bağlantı yakalandı";
        TxtHeading.Text = url is null ? "Yeni bir indirme başlat" : "Yeni bir indirme algılandı";

        // Adres ve dosya adi kutulari yalnizca elle indirmede gerekli. Tarayicidan
        // gelen baglantida ikisi de belli; gostermek sadece kalabalik olurdu.
        PnlManual.Visibility = url is null ? Visibility.Visible : Visibility.Collapsed;

        // Tarayicinin buldugu ad, adresten tahmin etmekten daima daha iyidir:
        // dosya barindirma siteleri adreste GUID kullaniyor, gercek adi
        // Content-Disposition'dan sadece tarayici gormus oluyor.
        if (_hasSuggestedName) SetFileName(suggestedName!, force: true);

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = false;

        if (string.IsNullOrWhiteSpace(TxtUrl.Text))
        {
            TxtUrl.Focus();
            TxtSize.Text = "—";
            TxtStatus.Text = "Bir adres yapıştırın.";
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
            return;
        }

        TxtStatus.Text = "";
        TxtSource.Text = Uri.TryCreate(url, UriKind.Absolute, out var host) ? host.Host : "—";

        _probeCts?.Cancel();
        _probeCts = new CancellationTokenSource();
        var ct = _probeCts.Token;

        TxtSize.Text = "sorgulanıyor…";
        SetFileName(UrlHelper.GuessFileName(url));

        var info = await RemoteProbe.ProbeAsync(url, ct);
        if (ct.IsCancellationRequested) return;

        // Tarayicinin verdigi ad uzantisizsa (Chrome bazen adresi GUID olan
        // dosyalarda boyle veriyor) sunucunun Content-Disposition adi daha iyidir.
        var hintHasExtension = Path.HasExtension(_fileName);
        if (!hintHasExtension && Path.HasExtension(info.FileName)) SetFileName(info.FileName, force: true);
        else SetFileName(info.FileName);

        TxtSize.Text = info.Size > 0 ? Format.Bytes(info.Size) : "bilinmiyor";

        if (info.Size > 0 && !info.Resumable)
            TxtStatus.Text = "Sunucu parçalı indirmeyi desteklemiyor.";
    }

    private void SetFileName(string name, bool force = false)
    {
        // Tarayicidan gelen ad varsa yoklamanin tahmini onu ezmesin.
        if (force || (!_userEditedFileName && !_hasSuggestedName))
        {
            _fileName = name;
            var suppress = _loading;
            _loading = true;
            TxtFileName.Text = name;
            _loading = suppress;
        }

        TxtKind.Text = string.IsNullOrWhiteSpace(_fileName) ? "—" : Icons.KindOf(_fileName);
    }

    private void TxtFileName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading) _userEditedFileName = true;
        _fileName = TxtFileName.Text.Trim();
        TxtKind.Text = string.IsNullOrWhiteSpace(_fileName) ? "—" : Icons.KindOf(_fileName);
    }

    private async void TxtUrl_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        _userEditedFileName = false;
        await ProbeAsync();
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Kayıt klasörü seç",
            InitialDirectory = Directory.Exists(_folder) ? _folder : _cfg.DownloadDir,
        };
        if (dlg.ShowDialog(this) == true)
        {
            _folder = dlg.FolderName;
            TxtFolder.Text = _folder;
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
            FileName = _fileName,
            TargetDir = _folder,
            Connections = Math.Clamp(_cfg.ConnectionsPerDownload, 1, 16),
            SpeedLimitBytes = 0,
            StartPaused = paused,
            OpenFolder = _cfg.OpenFolderAfterDownloadDefault,
            Referrer = _referrer,
            Cookie = _cookie,
            UserAgent = _userAgent,
        };

        try
        {
            SetBusy(true, "Kuyruğa ekleniyor…");
            using var client = new Aria2Client(_cfg);
            var gid = await DownloadService.EnqueueAsync(client, _cfg, req);

            // Ilerleme penceresi diyalogdan ONCE acilir: bu surecin son penceresi
            // kapaninca uygulama sonlaniyor, once yenisini acmazsak sureç olur ve
            // indirme baslar ama kullanici hicbir sey gormez.
            if (!paused) new MiniWindow(_cfg, gid).Show();

            Close();
        }
        catch (Exception ex)
        {
            SetBusy(false, "");
            Log.Error("Kuyruğa eklenemedi", ex);
            Warn("İndirme başlatılamadı: " + ex.Message);
        }
    }

    private void SetBusy(bool busy, string status)
    {
        BtnDownload.IsEnabled = !busy;
        BtnLater.IsEnabled = !busy;
        TxtStatus.Text = status;
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "Akıntı", MessageBoxButton.OK, MessageBoxImage.Warning);
}
