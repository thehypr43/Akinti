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
/// Odagi calmaz (ShowActivated=false), gorev cubugunda yer kaplamaz, sag altta
/// durur. Kapatmak indirmeyi durdurmaz, sadece pencereyi gizler.
/// </summary>
public partial class MiniWindow : Window
{
    private readonly Config _cfg;
    private readonly DispatcherTimer _timer;
    private Aria2Client? _client;
    private string _gid;
    private bool _finished;
    private string _filePath = "";

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
        PlaceBottomRight();
        if (!_cfg.IsConfigured) { Close(); return; }

        _client = new Aria2Client(_cfg);
        await RefreshAsync();
        _timer.Start();
    }

    /// <summary>Sag alt kose, gorev cubugunun uzerinde.</summary>
    private void PlaceBottomRight()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 24;
        Top = area.Bottom - ActualHeight - 24;
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
        if (!string.IsNullOrEmpty(item.FilePath)) _filePath = item.FilePath!;

        Bar.Value = item.Progress * 100;
        Bar.Foreground = item.Status == Aria2Status.Error
            ? (System.Windows.Media.Brush)FindResource("B.Danger")
            : (System.Windows.Media.Brush)FindResource("B.Accent");

        var host = string.IsNullOrEmpty(item.SourceUri) ? ""
            : Uri.TryCreate(item.SourceUri, UriKind.Absolute, out var u) ? u.Host : "";

        switch (item.Status)
        {
            case Aria2Status.Complete:
                ShowFinished("İndirme tamamlandı", "",
                    Format.Bytes(item.TotalLength) + (host.Length > 0 ? "  ·  " + host : ""));
                return;

            case Aria2Status.Error:
                ShowFinished("İndirme başarısız", "",
                    string.IsNullOrWhiteSpace(item.ErrorMessage)
                        ? $"aria2 hata kodu {item.ErrorCode}" : item.ErrorMessage!);
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

    private void ShowFinished(string header, string glyph, string meta)
    {
        if (_finished) return;
        _finished = true;
        _timer.Stop();

        TxtHeader.Text = header;
        TxtMeta.Text = meta;
        TxtPct.Text = "";
        PnlStats.Visibility = Visibility.Collapsed;
        PnlActions.Visibility = Visibility.Collapsed;
        TxtNote.Text = "";
        BtnOpenFolder.Visibility = _filePath.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Basarili bittiyse kendiliginden kapansin; hata varsa kullanici gorsun diye kalsin.
        if (header.Contains("tamamlandı", StringComparison.OrdinalIgnoreCase))
        {
            var close = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            close.Tick += (_, _) => { close.Stop(); Close(); };
            close.Start();
        }
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

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_filePath.Length > 0) PostActions.OpenFolder(_filePath);
        Close();
    }
}
