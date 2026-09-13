using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace HafifDM.Ui;

public enum RowState { Active, Waiting, Paused, Completed, Failed, Cancelled }

/// <summary>
/// Listede gorunen tek satir. Canli (aria2) ve gecmis (history.json) kayitlarini birlestirir.
/// Gorunume ait tum turetilmis alanlar (rozet rengi, ikon, metinler) burada uretilir;
/// XAML sadece baglar.
/// </summary>
public sealed class DownloadRow : INotifyPropertyChanged
{
    // Nocturne rozet renkleri
    private static readonly Brush TagAccentBg   = Frozen("#FF423A6A");
    private static readonly Brush TagAccentFg   = Frozen("#FFF5F4FF");
    private static readonly Brush TagNeutralBg  = Frozen("#FF3F424D");
    private static readonly Brush TagNeutralFg  = Frozen("#FFF3F5FE");
    private static readonly Brush TagDangerBg   = Frozen("#FF4A2B2F");
    private static readonly Brush TagDangerFg   = Frozen("#FFF6DADD");

    private static readonly Brush BarAccent  = Frozen("#FF9184D9");
    private static readonly Brush BarMuted   = Frozen("#FF595D6C");
    private static readonly Brush BarDanger  = Frozen("#FFE06C75");
    private static readonly Brush BarDone    = Frozen("#FFA7A1DB");

    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public string Gid { get; init; } = "";

    private string _name = "";
    public string Name { get => _name; set { if (Set(ref _name, value)) Changed(nameof(IconGlyph)); } }

    private RowState _state;
    public RowState State
    {
        get => _state;
        set { if (Set(ref _state, value)) RefreshAll(); }
    }

    private long _total;
    public long Total { get => _total; set { if (Set(ref _total, value)) RefreshProgress(); } }

    private long _completed;
    public long Completed { get => _completed; set { if (Set(ref _completed, value)) RefreshProgress(); } }

    private long _speed;
    public long Speed { get => _speed; set { if (Set(ref _speed, value)) RefreshProgress(); } }

    private long _uploadSpeed;
    public long UploadSpeed { get => _uploadSpeed; set { if (Set(ref _uploadSpeed, value)) Changed(nameof(MetaText)); } }

    private string? _error;
    public string? Error { get => _error; set { if (Set(ref _error, value)) Changed(nameof(MetaText)); } }

    private string? _note;
    public string? Note { get => _note; set { if (Set(ref _note, value)) Changed(nameof(MetaText)); } }

    private string _filePath = "";
    public string FilePath { get => _filePath; set { if (Set(ref _filePath, value)) Changed(nameof(IconGlyph)); } }

    public string SourceUri { get; set; } = "";
    public string? InfoHash { get; set; }
    public bool IsTorrent { get; set; }
    public DateTime? FinishedAt { get; set; }

    // ──────────── Gorunum alanlari ────────────

    public double Percent => Total > 0 ? Math.Clamp(Completed * 100.0 / Total, 0, 100) : 0;

    public string PercentText => State == RowState.Failed ? "" : $"{(int)Percent}%";

    /// <summary>Dosya turune gore Segoe Fluent Icons karakteri.</summary>
    public string IconGlyph =>
        Icons.ForFile(string.IsNullOrEmpty(FilePath) ? Name : FilePath, IsTorrent);

    public string TagText => State switch
    {
        RowState.Active => "İNDİRİLİYOR",
        RowState.Waiting => "KUYRUKTA",
        RowState.Paused => "DURAKLATILDI",
        RowState.Completed => "TAMAMLANDI",
        RowState.Failed => "BAŞARISIZ",
        RowState.Cancelled => "İPTAL EDİLDİ",
        _ => "",
    };

    public Brush TagBackground => State switch
    {
        RowState.Active => TagAccentBg,
        RowState.Failed => TagDangerBg,
        _ => TagNeutralBg,
    };

    public Brush TagForeground => State switch
    {
        RowState.Active => TagAccentFg,
        RowState.Failed => TagDangerFg,
        _ => TagNeutralFg,
    };

    public Brush BarBrush => State switch
    {
        RowState.Active => BarAccent,
        RowState.Completed => BarDone,
        RowState.Failed => BarDanger,
        _ => BarMuted,
    };

    /// <summary>Tasarimdaki alt satir: boyut · kaynak (ya da bitis bilgisi).</summary>
    public string MetaText
    {
        get
        {
            if (State == RowState.Failed)
                return Error ?? "bilinmeyen hata";

            if (State is RowState.Completed or RowState.Cancelled)
            {
                var parts = new List<string>();
                if (Total > 0) parts.Add(Format.Bytes(Total));
                if (!string.IsNullOrEmpty(Host)) parts.Add(Host);
                if (FinishedAt is { } t) parts.Add(t.ToString("dd.MM.yyyy HH:mm"));
                if (!string.IsNullOrEmpty(Note)) parts.Add(Note!);
                return string.Join("  ·  ", parts);
            }

            var meta = new List<string>();
            // Boyut henuz bilinmiyorsa "-" yazmak yerine alani hic gosterme.
            if (Total > 0) meta.Add($"{Format.Bytes(Completed)} / {Format.Bytes(Total)}");
            else if (Completed > 0) meta.Add(Format.Bytes(Completed));

            if (!string.IsNullOrEmpty(Host)) meta.Add(Host);
            if (IsTorrent && UploadSpeed > 0) meta.Add("↑ " + Format.Speed(UploadSpeed));
            return string.Join("  ·  ", meta);
        }
    }

    public string Host
    {
        get
        {
            if (string.IsNullOrEmpty(SourceUri)) return "";
            if (UrlHelper.IsMagnet(SourceUri)) return "magnet";
            return Uri.TryCreate(SourceUri, UriKind.Absolute, out var u) ? u.Host : "";
        }
    }

    public string SpeedText => State == RowState.Active && Speed > 0 ? Format.Speed(Speed) : "";

    public string EtaText => State == RowState.Active && Eta is { } e ? Format.Duration(e) + " kaldı" : "";

    public TimeSpan? Eta =>
        Speed > 0 && Total > Completed ? TimeSpan.FromSeconds((Total - Completed) / (double)Speed) : null;

    /// <summary>Duraklat / devam dugmesinin karakteri. Biten satirlarda dugme gizlenir.</summary>
    public string ToggleGlyph => State switch
    {
        RowState.Active => "\uE769",   // duraklat
        _ => "\uE768",                 // devam
    };

    public Visibility ToggleVisibility => IsFinished ? Visibility.Collapsed : Visibility.Visible;

    public bool IsFinished => State is RowState.Completed or RowState.Failed or RowState.Cancelled;
    public bool CanPause => State is RowState.Active or RowState.Waiting;
    public bool CanResume => State == RowState.Paused;
    public bool CanRetry => State is RowState.Failed or RowState.Cancelled;

    private void RefreshProgress()
    {
        Changed(nameof(Percent));
        Changed(nameof(PercentText));
        Changed(nameof(MetaText));
        Changed(nameof(SpeedText));
        Changed(nameof(EtaText));
    }

    private void RefreshAll()
    {
        RefreshProgress();
        Changed(nameof(TagText));
        Changed(nameof(TagBackground));
        Changed(nameof(TagForeground));
        Changed(nameof(BarBrush));
        Changed(nameof(ToggleGlyph));
        Changed(nameof(ToggleVisibility));
        Changed(nameof(IsFinished));
        Changed(nameof(CanPause));
        Changed(nameof(CanResume));
        Changed(nameof(CanRetry));
    }

    // ──────────── Donusturuculer ────────────

    public static RowState FromAria2(Aria2Status s) => s switch
    {
        Aria2Status.Active => RowState.Active,
        Aria2Status.Waiting => RowState.Waiting,
        Aria2Status.Paused => RowState.Paused,
        Aria2Status.Complete => RowState.Completed,
        Aria2Status.Error => RowState.Failed,
        Aria2Status.Removed => RowState.Cancelled,
        _ => RowState.Waiting,
    };

    public static DownloadRow From(DownloadItem item) => new DownloadRow { Gid = item.Gid }.UpdateFrom(item);

    public DownloadRow UpdateFrom(DownloadItem item)
    {
        Name = item.DisplayName;
        Total = item.TotalLength;
        Completed = item.CompletedLength;
        Speed = item.DownloadSpeed;
        UploadSpeed = item.UploadSpeed;
        Error = item.Status == Aria2Status.Error
            ? (string.IsNullOrWhiteSpace(item.ErrorMessage)
                ? $"aria2 hata kodu {item.ErrorCode}" : item.ErrorMessage)
            : null;
        if (!string.IsNullOrEmpty(item.FilePath)) FilePath = item.FilePath!;
        if (!string.IsNullOrEmpty(item.SourceUri)) SourceUri = item.SourceUri!;
        InfoHash = item.InfoHash;
        IsTorrent = item.IsTorrent;
        State = FromAria2(item.Status);   // en son: tum turetilmis alanlari tazeler
        return this;
    }

    public static DownloadRow From(HistoryEntry h) => new()
    {
        Gid = h.Gid,
        Name = h.Name,
        Total = h.Size,
        Completed = h.Status == HistoryStatus.Completed ? h.Size : 0,
        Error = h.ErrorMessage,
        Note = h.PostActionNote,
        FilePath = h.FilePath,
        SourceUri = h.SourceUri,
        IsTorrent = h.IsTorrent,
        FinishedAt = h.FinishedAt,
        State = h.Status switch
        {
            HistoryStatus.Completed => RowState.Completed,
            HistoryStatus.Failed => RowState.Failed,
            _ => RowState.Cancelled,
        },
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Changed(name!);
        return true;
    }
}
