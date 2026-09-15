using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Akinti.Ui;

/// <summary>
/// Ilerleme penceresi.
///
/// Bu pencere olmadan indirme baslattiktan sonra HICBIR geri bildirim yoktu:
/// tepsi ikonu yok (istenmedi), bildirim yok (istenmedi), ana pencere de kapali.
/// Dosya arka planda sessizce iniyordu.
///
/// Ekranin ortasinda acilir, cunku indirme diyalogunun yerini alir: diyalog
/// kapanir, bu acilir. Kapatmak indirmeyi durdurmaz, sadece pencereyi gizler.
/// Indirme bitince kendini kapatip yerine DoneWindow acar.
/// </summary>
public partial class MiniWindow : Window
{
    private readonly Config _cfg;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private Aria2Client? _client;
    private string _gid;
    private bool _finished;

    /// <summary>Pencere indirmeyi en basindan gordu mu? Gormediyse gecen sure
    /// yanlis olur, o yuzden sonuc penceresinde "sure" hic gosterilmez.</summary>
    private bool _sawFromStart;
    private bool _firstSample = true;

    public MiniWindow(Config cfg, string gid)
    {
        InitializeComponent();
        WindowEffects.Attach(this);

        _cfg = cfg;
        _gid = gid;

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

        // Ilk yoklama indirmeyi bitmis bulduysa pencere burada zaten kapandi.
        // Sayaci yine de baslatmak, kapali pencerenin arkasinda saniyede bir
        // atilan olu bir istek birakiyordu.
        if (!_finished) _timer.Start();
    }

    // ──────────── Yenileme ────────────

    private async Task RefreshAsync()
    {
        if (_client is null || _finished) return;
        try
        {
            var item = await _client.TellStatusAsync(_gid);
            if (item is null) { Close(); return; }

            Apply(item);
        }
        catch (Exception ex)
        {
            Log.Warn("Ilerleme penceresi yenilenemedi: " + ex.Message);
        }
    }

    private void Apply(DownloadItem item)
    {
        if (_firstSample)
        {
            _firstSample = false;
            _sawFromStart = item.Progress < 0.02;
            _watch.Restart();
        }

        Bar.Value = item.Progress * 100;

        var host = string.IsNullOrEmpty(item.SourceUri) ? ""
            : Uri.TryCreate(item.SourceUri, UriKind.Absolute, out var u) ? u.Host : "";

        switch (item.Status)
        {
            case Aria2Status.Complete:
            case Aria2Status.Error:
                HandOffToDone(item);
                return;

            case Aria2Status.Removed:
                Close();
                return;
        }

        var paused = item.Status == Aria2Status.Paused;

        // Tasarimin basligi: "%64 · 1 dk 12 sn kaldi"
        var pct = $"%{(int)(item.Progress * 100)}";
        TxtHeadline.Text = paused
            ? pct + "  ·  duraklatıldı"
            : item.Eta is { } eta ? $"{pct}  ·  {Format.Duration(eta)} kaldı" : pct;

        TxtName.Text = item.DisplayName;

        TxtMeta.Text = item.TotalLength > 0
            ? $"{Format.Bytes(item.CompletedLength)} / {Format.Bytes(item.TotalLength)}"
            : Format.Bytes(item.CompletedLength);
        TxtSpeed.Text = item.DownloadSpeed > 0 ? Format.Speed(item.DownloadSpeed) : "—";

        TxtParts.Text = item.Connections > 0 ? item.Connections + " bölüm" : "—";
        TxtElapsed.Text = _sawFromStart ? _watch.Elapsed.ToString(@"mm\:ss") : "—";
        TxtHost.Text = host.Length > 0 ? host : "—";
        TxtFolder.Text = string.IsNullOrEmpty(item.Dir) ? "—" : Path.GetFileName(item.Dir.TrimEnd('\\', '/'));

        IcoToggle.Text = paused ? "\uE768" : "\uE769";
        TxtToggle.Text = paused ? "Devam et" : "Duraklat";
        Title = paused ? "Duraklatıldı" : "İndiriliyor";

        _ = UpdateCountsAsync(paused);
    }

    /// <summary>Bu pencere tek indirmeyi gosterir; arkada baskalari varsa
    /// alt satir bunu soylesin.</summary>
    private async Task UpdateCountsAsync(bool paused)
    {
        if (_client is null) return;
        try
        {
            var stat = await _client.GetGlobalStatAsync();
            var others = stat.NumActive - 1;
            TxtNote.Text = others > 0 ? $"{others} indirme daha sürüyor"
                : stat.NumWaiting > 0 ? $"{stat.NumWaiting} indirme sırada" : "";
        }
        catch { TxtNote.Text = ""; }
    }

    /// <summary>Ilerleme penceresini kapatip sonuc penceresini acar.
    ///
    /// Sira onemli: once yeni pencere acilir, sonra bu kapanir. Ters sirada
    /// surecin son penceresi kapanmis olur ve uygulama sonlanir.</summary>
    private void HandOffToDone(DownloadItem item)
    {
        if (_finished) return;
        _finished = true;
        _timer.Stop();

        try { new DoneWindow(item, _sawFromStart ? _watch.Elapsed : null).Show(); }
        catch (Exception ex) { Log.Error("Sonuc penceresi acilamadi", ex); }

        Close();
    }

    // ──────────── Etkilesim ────────────

    private void BtnHide_Click(object sender, RoutedEventArgs e) => Close();

    private void BtnExpand_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.ExePath) { UseShellExecute = true });
            Close();
        }
        catch (Exception ex)
        {
            Log.Error("Ana pencere acilamadi", ex);
        }
    }

    private async void BtnToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null) return;
        try
        {
            var item = await _client.TellStatusAsync(_gid);
            if (item is null) return;
            if (item.Status == Aria2Status.Active) await _client.PauseAsync(_gid);
            else await _client.UnpauseAsync(_gid);
            await RefreshAsync();
        }
        catch (Exception ex) { Log.Warn("Duraklat/devam basarisiz: " + ex.Message); }
    }

    private async void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null) { Close(); return; }
        try { await _client.RemoveAsync(_gid); }
        catch (Exception ex) { Log.Warn("Iptal basarisiz: " + ex.Message); }
        Close();
    }
}
