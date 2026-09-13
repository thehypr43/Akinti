using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Akinti.Ui;

public partial class MainWindow : Window
{
    private const int SparkPoints = 42;
    private const double SparkW = 168, SparkH = 40;

    private readonly ObservableCollection<DownloadRow> _rows = [];
    private readonly Dictionary<string, DownloadRow> _byGid = [];
    private readonly List<double> _speedHistory = [];
    private readonly DispatcherTimer _timer;

    private Config _cfg = Config.Load();
    private Aria2Client? _client;
    private string _filter = "Active";
    private RadioButton? _lastNav;
    private bool _busy;
    private bool _allPaused;

    public MainWindow()
    {
        InitializeComponent();
        List.ItemsSource = _rows;
        _lastNav = NavActive;

        // Pencere gorunur degilken hic sorgu atilmaz; kapaliyken CPU tuketimi sifir.
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await RefreshAsync();

        Loaded += OnLoaded;
        Closed += (_, _) => { _timer.Stop(); _client?.Dispose(); };
        IsVisibleChanged += (_, _) => UpdateTimerState();
        StateChanged += (_, _) => { UpdateTimerState(); UpdateMaximizeGlyph(); };

        UpdateUrlPlaceholder();
    }

    // ──────────── Pencere kromu ────────────

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ToggleMaximize(); return; }
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnMaximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void UpdateMaximizeGlyph() =>
        IcoMaximize.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";

    private void UpdateTimerState()
    {
        var shouldRun = IsVisible && WindowState != WindowState.Minimized;
        if (shouldRun && !_timer.IsEnabled) _timer.Start();
        else if (!shouldRun && _timer.IsEnabled) _timer.Stop();
    }

    // ──────────── Baslangic ────────────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateDiskInfo();
        UpdateCapLabel();

        if (!_cfg.IsConfigured)
        {
            TxtStatusLeft.Text = "Kurulum tamamlanmamış — scripts\\install.ps1 çalıştırılmalı.";
            LoadHistoryOnly();
            return;
        }

        TxtStatusLeft.Text = "Motor başlatılıyor…";
        if (!await Aria2Engine.EnsureRunningAsync(_cfg))
        {
            TxtStatusLeft.Text = "Motor başlatılamadı — akinti.log dosyasına bakın.";
            LoadHistoryOnly();
            return;
        }

        _client = new Aria2Client(_cfg);
        await RefreshAsync();
        UpdateTimerState();
    }

    // ──────────── Veri ────────────

    private void LoadHistoryOnly()
    {
        foreach (var h in HistoryStore.All()) Upsert(DownloadRow.From(h));
        ApplyFilter();
    }

    private async Task RefreshAsync()
    {
        if (_busy || _client is null) return;
        _busy = true;
        try
        {
            var live = await _client.GetAllAsync().ConfigureAwait(true);
            var stat = await _client.GetGlobalStatAsync().ConfigureAwait(true);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in live)
            {
                seen.Add(item.Gid);
                if (_byGid.TryGetValue(item.Gid, out var row)) row.UpdateFrom(item);
                else Upsert(DownloadRow.From(item));
            }

            // aria2 yeniden basladiginda tamamlananlari unutur; kalicilik gecmiste.
            foreach (var h in HistoryStore.All())
            {
                if (_byGid.TryGetValue(h.Gid, out var existing))
                {
                    existing.Note ??= h.PostActionNote;
                    existing.FinishedAt ??= h.FinishedAt;
                    continue;
                }
                if (seen.Add(h.Gid)) Upsert(DownloadRow.From(h));
            }

            PushSpeed(stat.DownloadSpeed);
            UpdateHeader(stat);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            TxtStatusLeft.Text = "Motor ile bağlantı koptu.";
            Log.Warn("Yenileme hatası: " + ex.Message);
        }
        finally { _busy = false; }
    }

    private void Upsert(DownloadRow row)
    {
        _byGid[row.Gid] = row;
        _rows.Add(row);
    }

    private void UpdateHeader(GlobalStat stat)
    {
        var running = _rows.Count(r => r.State == RowState.Active);
        var paused = _rows.Count(r => r.State == RowState.Paused);
        var waiting = _rows.Count(r => r.State == RowState.Waiting);
        var done = _rows.Count(r => r.State == RowState.Completed);
        var failed = _rows.Count(r => r.State is RowState.Failed or RowState.Cancelled);

        CntActive.Text = (running + paused + waiting).ToString();
        CntQueue.Text = waiting.ToString();
        CntDone.Text = done.ToString();
        CntFailed.Text = failed.ToString();

        TxtHeaderSummary.Text = running > 0 || waiting > 0
            ? $"{running} indiriliyor · {waiting} kuyrukta"
            : "hazır";

        var mb = stat.DownloadSpeed / 1024.0 / 1024.0;
        TxtTotalSpeed.Text = mb.ToString("0.0");

        TxtStatusLeft.Text = _client is null ? "Motor kapalı" : "Motor çalışıyor";
        TxtStatusRight.Text = $"↓ {mb:0.0} MB/s  ·  sınır {CapLabel()}";

        _allPaused = running == 0 && paused > 0;
        IcoToggleAll.Text = _allPaused ? "\uE768" : "\uE769";
        TxtToggleAll.Text = _allPaused ? "Tümünü sürdür" : "Tümünü duraklat";

        (TxtSectionTitle.Text, TxtSectionSub.Text) = _filter switch
        {
            "Queue" => ("Kuyruk", "Aynı anda " + _cfg.MaxConcurrentDownloads +
                                  " aktarım çalışır, diğerleri sırada bekler."),
            "Done" => ("Tamamlanan", done + " kayıt"),
            "Failed" => ("Başarısız", failed + " kayıt"),
            _ => ("Etkin", $"{running} çalışıyor, {paused} duraklatıldı, {waiting} bekliyor"),
        };

        PnlSpeed.Visibility = _filter == "Active" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyFilter()
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_rows);
        view.Filter = o => o is DownloadRow r && _filter switch
        {
            "Queue" => r.State == RowState.Waiting,
            "Done" => r.State == RowState.Completed,
            "Failed" => r.State is RowState.Failed or RowState.Cancelled,
            _ => r.State is RowState.Active or RowState.Waiting or RowState.Paused,
        };
        view.Refresh();

        var empty = view.IsEmpty;
        PnlEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        List.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        if (empty)
        {
            (TxtEmptyTitle.Text, TxtEmptySub.Text) = _filter switch
            {
                "Queue" => ("Kuyruk boş", "Sıraya alınan aktarımlar burada bekler."),
                "Done" => ("Henüz tamamlanan yok", "Biten indirmeler burada listelenir."),
                "Failed" => ("Başarısız indirme yok", "İyi haber."),
                _ => ("İndirme yok",
                      "Tarayıcıda bir bağlantıya sağ tıklayıp Akıntı ile indir seçin ya da yukarıya bağlantı yapıştırın."),
            };
        }
    }

    // ──────────── Hiz grafigi ────────────

    private void PushSpeed(long bytesPerSecond)
    {
        _speedHistory.Add(bytesPerSecond / 1024.0 / 1024.0);
        if (_speedHistory.Count > SparkPoints) _speedHistory.RemoveAt(0);

        // Hic trafik yokken duz bir cizgi cizmek yerine grafigi bos birak.
        if (_speedHistory.Count < 2 || _speedHistory.All(v => v <= 0.001))
        {
            SparkLine.Points = [];
            SparkArea.Points = [];
            return;
        }

        // Olcek: en yuksek degere gore, ama tepe noktasi ust kenara yapismasin.
        var max = Math.Max(_speedHistory.Max(), 0.1) * 1.15;
        var step = SparkW / (SparkPoints - 1);
        var offset = SparkW - (_speedHistory.Count - 1) * step;

        var line = new PointCollection(_speedHistory.Count);
        for (var i = 0; i < _speedHistory.Count; i++)
        {
            var x = offset + i * step;
            var y = SparkH - 2 - (_speedHistory[i] / max) * (SparkH - 6);
            line.Add(new Point(x, y));
        }

        var area = new PointCollection(line) { new Point(SparkW, SparkH), new Point(offset, SparkH) };
        SparkLine.Points = line;
        SparkArea.Points = area;
    }

    // ──────────── Kenar cubugu bilgileri ────────────

    private void UpdateDiskInfo()
    {
        try
        {
            var dir = _cfg.DownloadDir;
            TxtDiskName.Text = Path.GetFileName(dir.TrimEnd('\\')) is { Length: > 0 } n ? n : dir;

            var root = Path.GetPathRoot(Path.GetFullPath(dir));
            if (string.IsNullOrEmpty(root)) return;

            var drive = new DriveInfo(root);
            if (!drive.IsReady) return;

            TxtDiskFree.Text = Format.Bytes(drive.AvailableFreeSpace) + " boş";
            BarDisk.Value = 100.0 * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize;
        }
        catch (Exception ex)
        {
            TxtDiskFree.Text = "—";
            Log.Warn("Disk bilgisi okunamadı: " + ex.Message);
        }
    }

    private string CapLabel() =>
        _cfg.GlobalSpeedLimitBytes > 0 ? Format.Speed(_cfg.GlobalSpeedLimitBytes) : "Sınırsız";

    private void UpdateCapLabel() => TxtCap.Text = CapLabel();

    // ──────────── Gezinme ────────────

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string tag) return;

        if (tag == "Settings")
        {
            // Ayarlar bir sayfa degil, kendi penceresi. Secimi eski sekmeye geri al.
            Dispatcher.BeginInvoke(() =>
            {
                (_lastNav ?? NavActive).IsChecked = true;
                OpenSettings();
            });
            return;
        }

        _lastNav = rb;
        _filter = tag;
        if (IsLoaded) { ApplyFilter(); _ = RefreshAsync(); }
    }

    private void OpenSettings()
    {
        var win = new SettingsWindow(_cfg) { Owner = this };
        if (win.ShowDialog() != true) return;

        _cfg = Config.Load();
        _client?.Dispose();
        _client = _cfg.IsConfigured ? new Aria2Client(_cfg) : null;
        UpdateDiskInfo();
        UpdateCapLabel();
    }

    // ──────────── Baglanti cubugu ────────────

    private void UpdateUrlPlaceholder() =>
        TxtUrlPlaceholder.Visibility = string.IsNullOrEmpty(TxtUrl.Text)
            ? Visibility.Visible : Visibility.Collapsed;

    private void TxtUrl_TextChanged(object sender, TextChangedEventArgs e) => UpdateUrlPlaceholder();

    private void TxtUrl_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { AddFromUrlBar(); e.Handled = true; }
    }

    private void BtnAddUrl_Click(object sender, RoutedEventArgs e) => AddFromUrlBar();

    private void AddFromUrlBar()
    {
        var url = TxtUrl.Text.Trim();
        if (url.Length == 0) { TxtUrl.Focus(); return; }

        if (!UrlHelper.IsSupportedScheme(url))
        {
            Warn("Desteklenmeyen adres. http, https, ftp, sftp ya da magnet olmalı.");
            return;
        }

        // Adres cubugundan eklemek de diyalogdan gecer: her indirme goz onunde onaylanir.
        new NewDownloadWindow(url) { Owner = this }.Show();
        TxtUrl.Clear();
    }

    private void BtnNew_Click(object sender, RoutedEventArgs e) =>
        // (string?)null sart: CapturedRequest asiri yuklemesi eklendiginden
        // duz null hangi kurucuya gidecegi belirsiz kaliyor.
        new NewDownloadWindow((string?)null) { Owner = this }.Show();

    private async void BtnToggleAll_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null) return;
        try
        {
            foreach (var row in _rows.Where(r => !r.IsFinished).ToList())
            {
                if (_allPaused && row.State == RowState.Paused) await _client.UnpauseAsync(row.Gid);
                else if (!_allPaused && row.CanPause) await _client.PauseAsync(row.Gid);
            }
            await RefreshAsync();
        }
        catch (Exception ex) { Warn("İşlem başarısız: " + ex.Message); }
    }

    // ──────────── Satir eylemleri ────────────

    private DownloadRow? Selected => List.SelectedItem as DownloadRow;

    private static DownloadRow? RowOf(object sender) =>
        (sender as FrameworkElement)?.Tag as DownloadRow;

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private async void RowToggle_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row || _client is null) return;
        try
        {
            if (row.State == RowState.Active) await _client.PauseAsync(row.Gid);
            else await _client.UnpauseAsync(row.Gid);
            await RefreshAsync();
        }
        catch (Exception ex) { Warn("İşlem başarısız: " + ex.Message); }
    }

    private async void RowRemove_Click(object sender, RoutedEventArgs e)
    {
        var row = RowOf(sender) ?? Selected;
        if (row is null) return;

        try
        {
            if (_client is not null)
            {
                if (row.IsFinished) await _client.RemoveResultAsync(row.Gid);
                else await _client.RemoveAsync(row.Gid);
            }
        }
        catch { /* aria2 kaydi zaten unutmus olabilir */ }

        HistoryStore.Remove(row.Gid);
        _rows.Remove(row);
        _byGid.Remove(row.Gid);
        ApplyFilter();
    }

    private void CtxOpen_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { FilePath.Length: > 0 } row) PostActions.OpenFolder(row.FilePath);
    }

    private async void CtxExtract_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { FilePath.Length: > 0 } row) return;
        if (!Archivers.IsArchive(row.FilePath)) { Warn("Bu dosya bir arşiv değil."); return; }

        var result = await Archivers.ExtractAsync(row.FilePath, _cfg);
        row.Note = result.Message;
        if (!result.Success) Warn(result.Message);
    }

    private void CtxRunAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { FilePath.Length: > 0 } row) row.Note = PostActions.RunAsAdmin(row.FilePath);
    }

    private void CtxRetry_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { SourceUri.Length: > 0 } row)
            new NewDownloadWindow(row.SourceUri) { Owner = this }.Show();
    }

    private void CtxCopyUrl_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { SourceUri.Length: > 0 } row) TrySetClipboard(row.SourceUri);
    }

    private void CtxCopyHash_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { InfoHash.Length: > 0 } row) TrySetClipboard(row.InfoHash!);
        else Warn("Bu kayıt bir torrent değil.");
    }

    private static void TrySetClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (Exception ex) { Log.Warn("Panoya yazılamadı: " + ex.Message); }
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "Akıntı", MessageBoxButton.OK, MessageBoxImage.Warning);
}
