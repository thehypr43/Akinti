using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Akinti.Ui;

/// <summary>
/// Kucuk ilerleme penceresi.
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
    private Aria2Client? _client;
    private string _gid;
    private bool _finished;

    public MiniWindow(Config cfg, string gid)
    {
        InitializeComponent();
        _cfg = cfg;
        _gid = gid;

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
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
            if (item is null) { Close(); return; }

            Apply(item);
        }
        catch (Exception ex)
        {
            Log.Warn("Mini pencere yenilenemedi: " + ex.Message);
        }
    }

    private void Apply(DownloadItem item)
    {
        TxtName.Text = item.DisplayName;
        IcoFile.Text = Icons.ForFile(item.FilePath ?? item.DisplayName, item.IsTorrent);

        Bar.Value = item.Progress * 100;
        Bar.Foreground = item.Status == Aria2Status.Error
            ? (System.Windows.Media.Brush)FindResource("B.Danger")
            : (System.Windows.Media.Brush)FindResource("B.Accent");

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

        TxtHeader.Text = item.Status == Aria2Status.Paused ? "Duraklatıldı" : "İndiriliyor";
        TxtPct.Text = $"{(int)(item.Progress * 100)}%";

        TxtMeta.Text = item.TotalLength > 0
            ? $"{Format.Bytes(item.CompletedLength)} / {Format.Bytes(item.TotalLength)}"
              + (host.Length > 0 ? "  ·  " + host : "")
            : host;

        TxtParts.Text = item.IsTorrent
            ? (item.NumSeeders > 0 ? item.NumSeeders + " kaynak" : "—")
            : (item.Connections > 0 ? item.Connections.ToString() : "—");
        TxtRemaining.Text = item.TotalLength > item.CompletedLength
            ? Format.Bytes(item.TotalLength - item.CompletedLength) : "—";
        TxtSpeed.Text = item.DownloadSpeed > 0 ? Format.Speed(item.DownloadSpeed) : "—";
        TxtEta.Text = item.Eta is { } eta ? Format.Duration(eta) : "—";

        IcoToggle.Text = item.Status == Aria2Status.Active ? "" : "";

        _ = UpdateQueueNoteAsync();
    }

    /// <summary>Tasarimdaki "miniQueueNote": bu pencere tek indirmeyi gosterir,
    /// arkada baskalari varsa en azindan sayisi yazsin.</summary>
    private async Task UpdateQueueNoteAsync()
    {
        if (_client is null) return;
        try
        {
            var stat = await _client.GetGlobalStatAsync();
            var others = stat.NumActive + stat.NumWaiting - 1;
            TxtNote.Text = others > 0 ? $"{others} indirme daha sürüyor" : "";
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

        try { new DoneWindow(item).Show(); }
        catch (Exception ex) { Log.Error("Sonuc penceresi acilamadi", ex); }

        Close();
    }

    // ──────────── Etkilesim ────────────

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

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
