using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace HafifDM.Ui;

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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// Torrent icindeki dosyalari secmek icin. Magnet eklendiginde dosya listesi ancak
/// metadata indikten sonra belli olur, o yuzden pencere once bekler.
/// Bekleme suresince indirme zaten devam eder; secim yapilmazsa hepsi iner.
/// </summary>
public partial class TorrentFilesWindow : Window
{
    private readonly Config _cfg;
    private readonly ObservableCollection<TorrentFileRow> _files = [];
    private readonly CancellationTokenSource _cts = new();
    private string _gid;

    public TorrentFilesWindow(Config cfg, string gid)
    {
        InitializeComponent();
        _cfg = cfg;
        _gid = gid;
        ListFiles.ItemsSource = _files;
        Closed += (_, _) => _cts.Cancel();
        Loaded += async (_, _) => await WaitForMetadataAsync();
    }

    private async Task WaitForMetadataAsync()
    {
        using var client = new Aria2Client(_cfg);
        var deadline = DateTime.UtcNow.AddMinutes(5);

        while (!_cts.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            try
            {
                // Magnet ise asil torrent yeni bir GID'e tasinir; onu takip et.
                var followed = await client.GetFollowedByAsync(_gid, _cts.Token);
                if (followed.Length > 0) _gid = followed[0];

                var status = await client.TellStatusAsync(_gid, _cts.Token);
                if (status is null)
                {
                    TxtSub.Text = "Bu indirme artik kuyrukta degil.";
                    return;
                }

                var files = await client.GetFilesAsync(_gid, _cts.Token);

                // Metadata gelmeden once aria2 tek bir sahte kayit doner ("[METADATA]...").
                var ready = files.Count > 0
                            && !files[0].Path.Contains("[METADATA]", StringComparison.Ordinal)
                            && files.Any(f => f.Length > 0);

                if (ready)
                {
                    Populate(status, files);
                    return;
                }

                TxtTitle.Text = status.DisplayName;
            }
            catch (Exception ex)
            {
                Log.Warn("Torrent bilgileri beklenirken hata: " + ex.Message);
            }

            try { await Task.Delay(1000, _cts.Token); }
            catch (OperationCanceledException) { return; }
        }

        if (!_cts.IsCancellationRequested)
            TxtSub.Text = "Torrent bilgileri 5 dakikada gelmedi. Indirme devam ediyor, tum dosyalar inecek.";
    }

    private void Populate(DownloadItem status, List<TorrentFile> files)
    {
        TxtTitle.Text = status.DisplayName;

        // Ortak kok klasoru kirp ki liste okunabilir olsun.
        var prefix = status.Dir.TrimEnd('\\', '/');

        _files.Clear();
        foreach (var f in files.OrderBy(f => f.Index))
        {
            var display = f.Path;
            if (prefix.Length > 0 && display.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                display = display[prefix.Length..].TrimStart('\\', '/');

            _files.Add(new TorrentFileRow
            {
                Index = f.Index,
                Display = display,
                Length = f.Length,
                Selected = f.Selected,
            });
        }

        var total = files.Sum(f => f.Length);
        TxtSub.Text = $"{files.Count} dosya, toplam {Format.Bytes(total)}. " +
                      "Istemediklerinin isaretini kaldirip Secimi uygula de.";
        BtnApply.IsEnabled = true;
    }

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
            MessageBox.Show(this, "Hicbir dosya secili degil. En az birini sec ya da indirmeyi iptal et.",
                "HafifDM", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            BtnApply.IsEnabled = false;
            using var client = new Aria2Client(_cfg);
            await client.ChangeOptionAsync(_gid, new Dictionary<string, string>
            {
                ["select-file"] = string.Join(',', chosen)
            });
            Log.Info($"Torrent dosya secimi uygulandi [{_gid}]: {chosen.Count}/{_files.Count}");
            Close();
        }
        catch (Exception ex)
        {
            BtnApply.IsEnabled = true;
            Log.Error("Dosya secimi uygulanamadi", ex);
            MessageBox.Show(this, "Secim uygulanamadi: " + ex.Message, "HafifDM",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
