using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace Akinti.Ui;

public sealed class TorrentFileRow : INotifyPropertyChanged
{
    public int Index { get; init; }
    public string Display { get; init; } = "";
    public long Length { get; init; }
    public string SizeText => Format.Bytes(Length);

    private bool _selected = true;
    public bool Selected
    {
        get => _selected;
        set { if (_selected != value) { _selected = value; OnPropertyChanged(); } }
    }

    private double _percent;
    public double Percent
    {
        get => _percent;
        set
        {
            if (Math.Abs(_percent - value) < 0.05) return;
            _percent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PercentText));
        }
    }

    public string PercentText => _percent >= 99.95 ? "tamam" : $"%{_percent:0}";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// Torrentin kendi penceresi.
///
/// Bir torrent sıradan bir indirme gibi davranmıyor: bittiğinde durmuyor,
/// paylaşmaya devam ediyor; hızı karşı taraftaki eşlerin sayısına bağlı; ve
/// içinden hangi dosyaların ineceği indirme sürerken bile değiştirilebiliyor.
/// Bu yüzden HTTP indirmelerinin penceresi (yüzde, kalan süre, bitti) torrent
/// için yanlış bir anlatım oluyordu — tohum/eş sayısı, yükleme hızı, paylaşım
/// oranı ve dosya listesi burada.
///
/// Magnet eklendiginde dosya listesi ancak metadata indikten sonra belli olur;
/// pencere o ana kadar bekler, indirme bu sırada zaten sürer.
/// </summary>
public partial class TorrentWindow : Window
{
    private readonly Config _cfg;
    private readonly ObservableCollection<TorrentFileRow> _files = [];
    private readonly DispatcherTimer _timer;
    private Aria2Client? _client;
    private string _gid;

    private bool _filesLoaded;
    private string _dir = "";
    private string _infoHash = "";
    private string _name = "";
    private bool _paused;

    public TorrentWindow(Config cfg, string gid)
    {
        InitializeComponent();
        WindowEffects.Attach(this);

        _cfg = cfg;
        _gid = gid;
        ListFiles.ItemsSource = _files;

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += async (_, _) => await RefreshAsync();

        Loaded += OnLoaded;
        Closed += (_, _) => { _timer.Stop(); _client?.Dispose(); };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_cfg.IsConfigured) { Close(); return; }
        _client = new Aria2Client(_cfg);
        await RefreshAsync();
        _timer.Start();
    }

    // ──────────── Yenileme ────────────

    private async Task RefreshAsync()
    {
        if (_client is null) return;
        try
        {
            // Magnet eklendiginde asil torrent yeni bir GID'e tasinir.
            var followed = await _client.GetFollowedByAsync(_gid);
            if (followed.Length > 0) _gid = followed[0];

            var item = await _client.TellStatusAsync(_gid);
            if (item is null)
            {
                TxtState.Text = "Kuyrukta değil";
                _timer.Stop();
                return;
            }

            Apply(item);

            var files = await _client.GetFilesAsync(_gid);
            ApplyFiles(files);
        }
        catch (Exception ex)
        {
            Log.Warn("Torrent penceresi yenilenemedi: " + ex.Message);
        }
    }

    private void Apply(DownloadItem item)
    {
        _name = item.DisplayName;
        _dir = item.Dir;
        _infoHash = item.InfoHash ?? "";
        _paused = item.Status == Aria2Status.Paused;

        Title = "Torrent — " + _name;
        TxtName.Text = _name;
        TxtPct.Text = $"{(int)(item.Progress * 100)}%";
        Bar.Value = item.Progress * 100;

        TxtMeta.Text = item.TotalLength > 0
            ? $"{Format.Bytes(item.CompletedLength)} / {Format.Bytes(item.TotalLength)}"
            : "Boyut torrent bilgileri gelince belli olur";

        TxtSeeders.Text = item.NumSeeders > 0 ? item.NumSeeders.ToString() : "—";
        TxtPeers.Text = item.Connections > 0 ? item.Connections.ToString() : "—";
        TxtDown.Text = item.DownloadSpeed > 0 ? Format.Speed(item.DownloadSpeed) : "—";
        TxtUp.Text = item.UploadSpeed > 0 ? Format.Speed(item.UploadSpeed) : "—";
        TxtUploaded.Text = item.UploadLength > 0 ? Format.Bytes(item.UploadLength) : "—";
        TxtRatio.Text = item.CompletedLength > 0
            ? item.Ratio.ToString("0.00", CultureInfo.CurrentCulture) : "—";

        TxtRemaining.Text = item.TotalLength > item.CompletedLength
            ? Format.Bytes(item.TotalLength - item.CompletedLength) : "—";
        TxtEta.Text = item.Eta is { } eta ? Format.Duration(eta) : "—";

        // Torrentin durumu "indiriliyor/bitti" ikilisine sigmiyor: paylasim
        // indirme bittikten sonra da surer ve kullanicinin gormesi gereken sey odur.
        var (state, brushKey) = item.Status switch
        {
            Aria2Status.Paused => ("Duraklatıldı", "B.TextFaint"),
            Aria2Status.Error => ("Hata", "B.Danger"),
            Aria2Status.Complete => ("Tamamlandı", "B.Success"),
            Aria2Status.Removed => ("Durduruldu", "B.TextFaint"),
            _ when item.IsSeeding => ("Paylaşılıyor", "B.Success"),
            _ => ("İndiriliyor", "B.Accent"),
        };
        TxtState.Text = state;
        TxtState.Foreground = (System.Windows.Media.Brush)FindResource(brushKey);

        var live = item.Status is Aria2Status.Active or Aria2Status.Paused or Aria2Status.Waiting;
        BtnToggle.Content = _paused ? "Devam et" : "Duraklat";
        BtnToggle.IsEnabled = live;
        BtnStopSeed.IsEnabled = live;
        BtnApply.IsEnabled = live && _filesLoaded;
        BtnCopyMagnet.IsEnabled = _infoHash.Length > 0;
    }

    private void ApplyFiles(List<TorrentFile> files)
    {
        // Metadata gelmeden once aria2 tek bir sahte kayit doner ("[METADATA]...").
        var ready = files.Count > 0
                    && !files[0].Path.Contains("[METADATA]", StringComparison.Ordinal)
                    && files.Any(f => f.Length > 0);
        if (!ready) return;

        if (!_filesLoaded)
        {
            _filesLoaded = true;
            ListFiles.Visibility = Visibility.Visible;
            TxtFilesEmpty.Visibility = Visibility.Collapsed;
            BtnApply.IsEnabled = true;

            // Ortak kok klasoru kirp ki liste okunabilir olsun. aria2 yollari
            // egik cizgiyle, kendi "dir" degerini ters cizgiyle veriyor.
            var prefix = _dir.Replace('/', '\\').TrimEnd('\\');

            var relative = files.OrderBy(f => f.Index).Select(f =>
            {
                var d = f.Path.Replace('/', '\\');
                if (prefix.Length > 0 && d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    d = d[prefix.Length..].TrimStart('\\');
                return d;
            }).ToList();

            // Cok dosyali torrentler tek bir kok klasor icinde gelir ve o klasorun
            // adi zaten pencerenin tepesinde yaziyor. Her satirda tekrarlamasin.
            var root = CommonRootFolder(relative);
            if (root is not null)
                relative = relative.Select(d => d[(root.Length + 1)..]).ToList();

            var ordered = files.OrderBy(f => f.Index).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                _files.Add(new TorrentFileRow
                {
                    Index = ordered[i].Index,
                    Display = relative[i],
                    Length = ordered[i].Length,
                    Selected = ordered[i].Selected,
                    Percent = ordered[i].Progress * 100,
                });
            }

            TxtFilesLabel.Text = $"DOSYALAR — {files.Count} dosya, {Format.Bytes(files.Sum(f => f.Length))}";
            return;
        }

        // Sonraki turlarda yalnizca ilerleme guncellenir; kullanicinin isaret
        // kutularina dokunulmaz.
        foreach (var f in files)
        {
            var row = _files.FirstOrDefault(r => r.Index == f.Index);
            if (row is not null) row.Percent = f.Progress * 100;
        }
    }

    /// <summary>Butun dosyalar ayni klasorle basliyorsa o klasorun adi.</summary>
    private static string? CommonRootFolder(List<string> paths)
    {
        if (paths.Count == 0) return null;

        var first = paths[0];
        var slash = first.IndexOf('\\');
        if (slash <= 0) return null;

        var root = first[..slash];
        return paths.All(p => p.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) ? root : null;
    }

    // ──────────── Dosya secimi ────────────

    private void BtnAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var f in _files) f.Selected = true;
    }

    private void BtnNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var f in _files) f.Selected = false;
    }

    private async void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        var chosen = _files.Where(f => f.Selected).Select(f => f.Index).ToList();
        if (chosen.Count == 0)
        {
            Warn("Hiçbir dosya seçili değil. En az birini seç ya da paylaşımı durdur.");
            return;
        }
        if (_client is null) return;

        try
        {
            BtnApply.IsEnabled = false;
            await _client.ChangeOptionAsync(_gid, new Dictionary<string, string>
            {
                ["select-file"] = string.Join(',', chosen)
            });
            Log.Info($"Torrent dosya secimi uygulandi [{_gid}]: {chosen.Count}/{_files.Count}");
        }
        catch (Exception ex)
        {
            Log.Error("Dosya secimi uygulanamadi", ex);
            Warn("Seçim uygulanamadı: " + ex.Message);
        }
        finally { BtnApply.IsEnabled = true; }
    }

    // ──────────── Eylemler ────────────

    private async void BtnToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null) return;
        try
        {
            if (_paused) await _client.UnpauseAsync(_gid);
            else await _client.PauseAsync(_gid);
            await RefreshAsync();
        }
        catch (Exception ex) { Warn("Duraklat/devam başarısız: " + ex.Message); }
    }

    /// <summary>Paylasimi bitirir. Indirilen dosyalara dokunulmaz; sadece aria2
    /// bu torrentle ilgilenmeyi birakir.</summary>
    private async void BtnStopSeed_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null) return;
        try
        {
            await _client.RemoveAsync(_gid);
            await RefreshAsync();
        }
        catch (Exception ex) { Warn("Paylaşım durdurulamadı: " + ex.Message); }
    }

    private void BtnFolder_Click(object sender, RoutedEventArgs e)
    {
        var first = _files.FirstOrDefault();
        var path = first is null ? _dir : Path.Combine(_dir, first.Display);
        PostActions.OpenFolder(path);
    }

    private void BtnCopyMagnet_Click(object sender, RoutedEventArgs e)
    {
        if (_infoHash.Length == 0) return;
        var magnet = "magnet:?xt=urn:btih:" + _infoHash;
        if (!string.IsNullOrWhiteSpace(_name)) magnet += "&dn=" + Uri.EscapeDataString(_name);
        try { Clipboard.SetText(magnet); }
        catch (Exception ex) { Log.Warn("Panoya yazılamadı: " + ex.Message); }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void Warn(string message) =>
        MessageBox.Show(this, message, "Akıntı", MessageBoxButton.OK, MessageBoxImage.Warning);
}
