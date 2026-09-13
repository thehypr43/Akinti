using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace HafifDM.Ui;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<DownloadRow> _rows = [];
    private readonly Dictionary<string, DownloadRow> _byGid = [];
    private readonly DispatcherTimer _timer;
    private Config _cfg = Config.Load();
    private Aria2Client? _client;
    private string _filter = "All";
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        List.ItemsSource = _rows;

        // Pencere gorunur degilken hic sorgu atilmaz; kapaliyken CPU tuketimi sifir.
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await RefreshAsync();

        Loaded += MainWindow_Loaded;
        Closed += (_, _) => { _timer.Stop(); _client?.Dispose(); };
        IsVisibleChanged += (_, _) => UpdateTimerState();
        StateChanged += (_, _) => UpdateTimerState();
        UpdateButtons();
    }

    private void UpdateTimerState()
    {
        var shouldRun = IsVisible && WindowState != WindowState.Minimized;
        if (shouldRun && !_timer.IsEnabled) _timer.Start();
        else if (!shouldRun && _timer.IsEnabled) _timer.Stop();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_cfg.IsConfigured)
        {
            TxtEngine.Text = "Kurulum tamamlanmamis. scripts\\install.ps1 dosyasini calistir.";
            LoadHistoryOnly();
            return;
        }

        TxtEngine.Text = "Motor baslatiliyor...";
        var ok = await Aria2Engine.EnsureRunningAsync(_cfg);
        if (!ok)
        {
            TxtEngine.Text = "Motor baslatilamadi. Ayrinti icin hafifdm.log dosyasina bak.";
            LoadHistoryOnly();
            return;
        }

        _client = new Aria2Client(_cfg);
        TxtEngine.Text = "Motor calisiyor";
        await RefreshAsync();
        UpdateTimerState();
    }

    // ---------------- Veri ----------------

    private void LoadHistoryOnly()
    {
        foreach (var h in HistoryStore.All())
            Upsert(DownloadRow.From(h));
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

            // Canli kayitlar once: en taze durum bunlarda.
            foreach (var item in live)
            {
                seen.Add(item.Gid);
                if (_byGid.TryGetValue(item.Gid, out var row)) row.UpdateFrom(item);
                else Upsert(DownloadRow.From(item));
            }

            // Gecmis: aria2 yeniden basladiginda tamamlananlari unutur, kalicilik burada.
            foreach (var h in HistoryStore.All())
            {
                if (!seen.Add(h.Gid) && _byGid.TryGetValue(h.Gid, out var existing))
                {
                    // aria2 hala biliyorsa sadece kanca notunu tamamla
                    existing.Note ??= h.PostActionNote;
                    existing.FinishedAt ??= h.FinishedAt;
                    continue;
                }
                if (!_byGid.ContainsKey(h.Gid)) Upsert(DownloadRow.From(h));
            }

            TxtTotalSpeed.Text = stat.NumActive > 0
                ? $"{Format.Speed(stat.DownloadSpeed)}  -  {stat.NumActive} aktif"
                : "Aktif indirme yok";

            ApplyFilter();
        }
        catch (Exception ex)
        {
            TxtEngine.Text = "Motor ile baglanti koptu.";
            Log.Warn("Yenileme hatasi: " + ex.Message);
        }
        finally { _busy = false; }
    }

    private void Upsert(DownloadRow row)
    {
        _byGid[row.Gid] = row;
        _rows.Add(row);
    }

    private void ApplyFilter()
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_rows);
        view.Filter = o => o is DownloadRow r && _filter switch
        {
            "Active" => r.State is RowState.Active or RowState.Waiting or RowState.Paused,
            "Completed" => r.State == RowState.Completed,
            "Failed" => r.State is RowState.Failed or RowState.Cancelled,
            "Torrent" => r.IsTorrent,
            _ => true,
        };
        view.Refresh();
        TxtEmpty.Visibility = view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------- Arayuz olaylari ----------------

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag })
        {
            _filter = tag;
            if (IsLoaded) ApplyFilter();
        }
    }

    private DownloadRow? Selected => List.SelectedItem as DownloadRow;

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void UpdateButtons()
    {
        var r = Selected;
        BtnPause.IsEnabled = r?.CanPause == true;
        BtnResume.IsEnabled = r?.CanResume == true;
        BtnCancel.IsEnabled = r?.CanCancel == true;
        BtnRetry.IsEnabled = r?.CanRetry == true;
        BtnOpen.IsEnabled = r is not null && !string.IsNullOrEmpty(r.FilePath);
        BtnRemove.IsEnabled = r is not null;
    }

    private void BtnNew_Click(object sender, RoutedEventArgs e) =>
        new NewDownloadWindow(null) { Owner = this }.Show();

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_cfg) { Owner = this };
        if (win.ShowDialog() == true)
        {
            _cfg = Config.Load();
            _client?.Dispose();
            _client = _cfg.IsConfigured ? new Aria2Client(_cfg) : null;
        }
    }

    private async void BtnPause_Click(object sender, RoutedEventArgs e) =>
        await Control(r => _client!.PauseAsync(r.Gid));

    private async void BtnResume_Click(object sender, RoutedEventArgs e) =>
        await Control(r => _client!.UnpauseAsync(r.Gid));

    private async void BtnCancel_Click(object sender, RoutedEventArgs e) =>
        await Control(r => _client!.RemoveAsync(r.Gid));

    private async Task Control(Func<DownloadRow, Task> action)
    {
        if (Selected is not { } row || _client is null) return;
        try { await action(row); await RefreshAsync(); }
        catch (Exception ex) { Warn("Islem basarisiz: " + ex.Message); }
    }

    private void BtnRetry_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row || string.IsNullOrEmpty(row.SourceUri)) return;
        // Yeniden deneme de diyalogdan gecer: her indirme goz onunde onaylanir.
        new NewDownloadWindow(row.SourceUri) { Owner = this }.Show();
    }

    private void BtnOpen_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { FilePath.Length: > 0 } row) PostActions.OpenFolder(row.FilePath);
    }

    private async void BtnRemove_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
        try
        {
            if (_client is not null && row.IsFinished)
                await _client.RemoveResultAsync(row.Gid);
        }
        catch { /* aria2 zaten unutmus olabilir */ }

        HistoryStore.Remove(row.Gid);
        _rows.Remove(row);
        _byGid.Remove(row.Gid);
        ApplyFilter();
    }

    // ---------------- Sag tik menusu ----------------

    private async void CtxExtract_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { FilePath.Length: > 0 } row) return;
        if (!Archivers.IsArchive(row.FilePath)) { Warn("Bu dosya bir arsiv degil."); return; }

        var result = await Archivers.ExtractAsync(row.FilePath, _cfg);
        row.Note = result.Message;
        if (!result.Success) Warn(result.Message);
    }

    private void CtxRunAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { FilePath.Length: > 0 } row) return;
        var note = PostActions.RunAsAdmin(row.FilePath);
        row.Note = note;
    }

    private void CtxCopyUrl_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { SourceUri.Length: > 0 } row) TrySetClipboard(row.SourceUri);
    }

    private void CtxCopyHash_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { InfoHash.Length: > 0 } row) TrySetClipboard(row.InfoHash!);
        else Warn("Bu kayit bir torrent degil.");
    }

    private static void TrySetClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (Exception ex) { Log.Warn("Panoya yazilamadi: " + ex.Message); }
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "HafifDM", MessageBoxButton.OK, MessageBoxImage.Warning);
}
