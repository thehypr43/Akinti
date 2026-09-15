using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace Akinti.Ui;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly ObservableCollection<DownloadRow> _rows = [];
    private readonly Dictionary<string, DownloadRow> _byGid = [];
    private readonly DispatcherTimer _timer;

    private Config _cfg = Config.Load();
    private Aria2Client? _client;
    private string _filter = "All";
    private string _search = "";
    private RadioButton? _lastNav;
    private bool _busy;
    private bool _allPaused;
    private bool _sidebarCollapsed;

    /// <summary>Siralama: 0 tarih, 1 ad, 2 boyut.</summary>
    private int _sort;

    private string _totalSpeedText = "0,0 MB/s";

    /// <summary>"Bugün" grubunun sagindaki canli toplam hiz.</summary>
    public string TotalSpeedText
    {
        get => _totalSpeedText;
        private set
        {
            if (_totalSpeedText == value) return;
            _totalSpeedText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalSpeedText)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        InitializeComponent();
        WindowEffects.Attach(this);
        DataContext = this;
        List.ItemsSource = _rows;
        _lastNav = NavAll;

        // Satirlar gune gore gruplanir; grup basliklari tasarimdaki "Bugün"/"Dün".
        var view = CollectionViewSource.GetDefaultView(_rows);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DownloadRow.GroupKey)));
        ApplySort(view);

        // Pencere gorunur degilken hic sorgu atilmaz; kapaliyken CPU tuketimi sifir.
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += async (_, _) => await RefreshAsync();

        Loaded += OnLoaded;
        Closed += (_, _) => { _timer.Stop(); _client?.Dispose(); };
        IsVisibleChanged += (_, _) => UpdateTimerState();
        StateChanged += (_, _) => UpdateTimerState();

        UpdateSearchPlaceholder();
    }

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

        CntAll.Text = _rows.Count.ToString();
        CntActive.Text = (running + waiting).ToString();
        PillActive.Visibility = running + waiting > 0 ? Visibility.Visible : Visibility.Collapsed;
        CntPaused.Text = paused.ToString();
        CntDone.Text = done.ToString();
        CntFailed.Text = failed.ToString();

        // Ozet, Windows'un kendi baslik cubugunda duruyor.
        Title = running > 0 || waiting > 0
            ? $"Akıntı — {running} indiriliyor · {waiting} kuyrukta"
            : "Akıntı";

        // Sabit MB/s birimi kucuk hizlarda hep "0,0" gosteriyordu; birim degere gore secilir.
        TotalSpeedText = stat.DownloadSpeed > 0 ? Format.Speed(stat.DownloadSpeed) : "0 KB/sn";

        _allPaused = running == 0 && paused > 0;
        TxtToggleAll.Text = _allPaused ? "Tümünü sürdür" : "Tümünü duraklat";

        TxtStatusLeft.Text = _client is null
            ? "Motor kapalı"
            : $"{running} etkin indirme  ·  Toplam {TotalSpeedText}";
        TxtStatusRight.Text = $"Hız sınırı: {CapLabel()}";

        TxtSectionTitle.Text = _filter switch
        {
            "Active" => "Devam edenler",
            "Paused" => "Duraklatılmış",
            "Done" => "Tamamlananlar",
            "Failed" => "Başarısız",
            _ => "İndirilenler",
        };
    }

    private void ApplyFilter()
    {
        var view = CollectionViewSource.GetDefaultView(_rows);
        view.Filter = o => o is DownloadRow r && r.Matches(_search) && _filter switch
        {
            "Active" => r.State is RowState.Active or RowState.Waiting,
            "Paused" => r.State == RowState.Paused,
            "Done" => r.State == RowState.Completed,
            "Failed" => r.State is RowState.Failed or RowState.Cancelled,
            _ => true,
        };
        view.Refresh();

        var empty = view.IsEmpty;
        PnlEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        List.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        if (!empty) return;

        if (_search.Length > 0)
        {
            TxtEmptyTitle.Text = "Eşleşen indirme yok";
            TxtEmptySub.Text = $"“{_search}” için bir sonuç bulunamadı.";
            return;
        }

        (TxtEmptyTitle.Text, TxtEmptySub.Text) = _filter switch
        {
            "Active" => ("Devam eden indirme yok", "Başlattığın indirmeler burada görünür."),
            "Paused" => ("Duraklatılmış indirme yok", "Duraklattıkların burada bekler."),
            "Done" => ("Henüz tamamlanan yok", "Biten indirmeler burada listelenir."),
            "Failed" => ("Başarısız indirme yok", "İyi haber."),
            _ => ("İndirme yok",
                  "Tarayıcıda bir bağlantıya sağ tıklayıp Akıntı ile indir seçin ya da Yeni indirme'ye basın."),
        };
    }

    private void ApplySort(ICollectionView view)
    {
        view.SortDescriptions.Clear();
        // Gruplar gune gore ayrildigi icin tarih sirasi gruplarin sirasini da belirler.
        switch (_sort)
        {
            case 1:
                view.SortDescriptions.Add(new SortDescription(nameof(DownloadRow.SortDate), ListSortDirection.Descending));
                view.SortDescriptions.Add(new SortDescription(nameof(DownloadRow.Name), ListSortDirection.Ascending));
                break;
            case 2:
                view.SortDescriptions.Add(new SortDescription(nameof(DownloadRow.SortDate), ListSortDirection.Descending));
                view.SortDescriptions.Add(new SortDescription(nameof(DownloadRow.Total), ListSortDirection.Descending));
                break;
            default:
                view.SortDescriptions.Add(new SortDescription(nameof(DownloadRow.SortDate), ListSortDirection.Descending));
                break;
        }
    }

    private void BtnSort_Click(object sender, RoutedEventArgs e)
    {
        _sort = (_sort + 1) % 3;
        TxtSort.Text = _sort switch { 1 => "Ad", 2 => "Boyut", _ => "Tarih" };
        ApplySort(CollectionViewSource.GetDefaultView(_rows));
    }

    // ──────────── Kenar cubugu ────────────

    /// <summary>Kenar cubugunu yaziya gerek birakmadan daraltir; ikonlar kalir.</summary>
    private void BtnMenu_Click(object sender, RoutedEventArgs e)
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        var vis = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;

        ColSidebar.Width = new GridLength(_sidebarCollapsed ? 64 : 232);
        foreach (var t in new[] { LblAll, LblActive, LblPaused, LblDone, LblFailed, LblSettings })
            t.Visibility = vis;
        foreach (var t in new FrameworkElement[] { CntAll, PillActive, CntPaused, CntDone, CntFailed, CardDisk })
            t.Visibility = t == PillActive && CntActive.Text == "0" ? Visibility.Collapsed : vis;

        BtnMenu.ToolTip = _sidebarCollapsed ? "Kenar çubuğunu genişlet" : "Kenar çubuğunu daralt";
    }

    private void UpdateDiskInfo()
    {
        try
        {
            var dir = _cfg.DownloadDir;
            var root = Path.GetPathRoot(Path.GetFullPath(dir));
            TxtDiskName.Text = "Disk · " + (string.IsNullOrEmpty(root) ? dir : root.TrimEnd('\\'));
            if (string.IsNullOrEmpty(root)) return;

            var drive = new DriveInfo(root);
            if (!drive.IsReady) return;

            var used = 100.0 * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize;
            BarDisk.Value = used;
            TxtDiskPct.Text = "%" + (int)Math.Round(used);
            TxtDiskFree.Text = Format.Bytes(drive.AvailableFreeSpace) + " boş alan kaldı";
        }
        catch (Exception ex)
        {
            TxtDiskFree.Text = "—";
            Log.Warn("Disk bilgisi okunamadı: " + ex.Message);
        }
    }

    private string CapLabel() =>
        _cfg.GlobalSpeedLimitBytes > 0 ? Format.Speed(_cfg.GlobalSpeedLimitBytes) : "Sınırsız";

    // ──────────── Gezinme ────────────

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string tag) return;

        if (tag == "Settings")
        {
            // Ayarlar bir sayfa degil, kendi penceresi. Secimi eski sekmeye geri al.
            Dispatcher.BeginInvoke(() =>
            {
                // Ayarlar satiri secili KALMAMALI: kalirsa ikinci kez tiklandiginda
                // Checked bir daha tetiklenmiyor ve pencere hic acilmiyordu.
                NavSettings.IsChecked = false;
                (_lastNav ?? NavAll).IsChecked = true;
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
    }

    // ──────────── Arama ────────────

    private void UpdateSearchPlaceholder() =>
        TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtSearch.Text)
            ? Visibility.Visible : Visibility.Collapsed;

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSearchPlaceholder();
        _search = TxtSearch.Text.Trim();
        ApplyFilter();
    }

    // ──────────── Arac cubugu ────────────

    private void BtnNew_Click(object sender, RoutedEventArgs e) =>
        // (string?)null sart: CapturedRequest asiri yuklemesi eklendiginden
        // duz null hangi kurucuya gidecegi belirsiz kaliyor.
        new NewDownloadWindow((string?)null) { Owner = this }.Show();

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e) =>
        PostActions.OpenFolder(_cfg.DownloadDir);

    /// <summary>Yalnizca BITMIS kayitlari listeden siler; suren indirmelere dokunmaz.</summary>
    private async void BtnClearList_Click(object sender, RoutedEventArgs e)
    {
        var finished = _rows.Where(r => r.IsFinished).ToList();
        if (finished.Count == 0) return;

        var answer = MessageBox.Show(this,
            $"{finished.Count} tamamlanmış kayıt listeden silinsin mi? Dosyalar diskte kalır.",
            "Akıntı", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;

        foreach (var row in finished)
        {
            try { if (_client is not null) await _client.RemoveResultAsync(row.Gid); }
            catch { /* aria2 kaydi zaten unutmus olabilir */ }
            HistoryStore.Remove(row.Gid);
            _rows.Remove(row);
            _byGid.Remove(row.Gid);
        }
        ApplyFilter();
    }

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

    private void RowOpen_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { FilePath.Length: > 0 } row) return;
        PostActions.OpenFile(row.FilePath);
    }

    private void RowRetry_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { SourceUri.Length: > 0 } row)
            new NewDownloadWindow(row.SourceUri) { Owner = this }.Show();
    }

    /// <summary>"…" dugmesi satirin kendi menusunu acar; sag tik menusuyle ayni.</summary>
    private void RowMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || RowOf(sender) is not { } row) return;

        List.SelectedItem = row;
        if (List.ContextMenu is not { } menu) return;

        menu.PlacementTarget = el;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
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

    private static void TrySetClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (Exception ex) { Log.Warn("Panoya yazılamadı: " + ex.Message); }
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "Akıntı", MessageBoxButton.OK, MessageBoxImage.Warning);
}
