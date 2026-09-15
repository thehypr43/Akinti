using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace Akinti.Ui;

public enum RowState { Active, Waiting, Paused, Completed, Failed, Cancelled }

/// <summary>
/// Listede gorunen tek satir. Canli (aria2) ve gecmis (history.json) kayitlarini birlestirir.
/// Gorunume ait tum turetilmis alanlar (rozet rengi, ikon, metinler) burada uretilir;
/// XAML sadece baglar.
/// </summary>
public sealed class DownloadRow : INotifyPropertyChanged
{
    /// <summary>Rozet ve cubuk renkleri paletten okunur.
    ///
    /// Burada sabit renk YOK: sabit yazilmis olanlar Windows temasi degisince
    /// (ve vurgu rengi degisince) yerinde kalip mor bir leke birakiyordu.
    /// Arama her cagrida yapiliyor, cunku palet calisma aninda degisebiliyor.</summary>
    private static Brush Res(string key, string fallback)
    {
        if (Application.Current?.TryFindResource(key) is Brush b) return b;
        var frozen = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
        frozen.Freeze();
        return frozen;
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
    public DateTime? FinishedAt { get; set; }

    // ──────────── Gorunum alanlari ────────────

    public double Percent => Total > 0 ? Math.Clamp(Completed * 100.0 / Total, 0, 100) : 0;

    public string PercentText => State == RowState.Failed ? "" : "%" + (int)Percent;

    /// <summary>Dosya karesinde yazan uzanti: ZIP, EXE, MP4... Tasarimda bu kare
    /// ikon degil, dosyanin uzantisini tasiyor.</summary>
    public string KindShort
    {
        get
        {
            var name = string.IsNullOrEmpty(FilePath) ? Name : FilePath;
            var ext = System.IO.Path.GetExtension(name).TrimStart('.');
            if (ext.Length == 0) return "DSY";
            return (ext.Length > 4 ? ext[..4] : ext).ToUpperInvariant();
        }
    }

    /// <summary>"8 bölüm" — aria2 su an kac baglanti kullaniyor.</summary>
    public string PartsText => State switch
    {
        RowState.Active when Connections > 0 => Connections + " bölüm",
        RowState.Waiting => "sırada",
        RowState.Paused => "beklemede",
        _ => "",
    };

    /// <summary>Satirin durumu, meta satirinin basinda renkli tek kelime.
    /// Surmekte olan indirmede yazilmaz: cubuk zaten onu soyluyor.</summary>
    public string StatusText => State switch
    {
        RowState.Waiting => "Kuyrukta",
        RowState.Paused => "Duraklatıldı",
        RowState.Completed => "Tamamlandı",
        RowState.Failed => "Başarısız",
        RowState.Cancelled => "İptal edildi",
        _ => "",
    };

    public Brush StatusBrush => State switch
    {
        RowState.Paused => Res("B.Admin", "#FF9A4F00"),
        RowState.Completed => Res("B.Success", "#FF0F7B0F"),
        RowState.Failed or RowState.Cancelled => Res("B.Danger", "#FFC42B1C"),
        _ => Res("B.TextMuted", "#FF808080"),
    };

    public Visibility StatusVisibility =>
        StatusText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SeparatorVisibility =>
        StatusText.Length > 0 && MetaText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Cubuk yalnizca surmekte olan satirlarda; biten satirda anlami yok.</summary>
    public Visibility ProgressVisibility =>
        State is RowState.Active or RowState.Paused or RowState.Waiting
            ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Yarim kalmis indirme yeniden denenirse kaldigi yerden devam eder.</summary>
    public string HintText =>
        State is RowState.Failed or RowState.Cancelled && Completed > 0 ? "Sürdürülebilir" : "";

    public Visibility HintVisibility =>
        HintText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RetryVisibility => CanRetry ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OpenVisibility =>
        State == RowState.Completed ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Listeyi gune gore bolen baslik.</summary>
    public string GroupKey
    {
        get
        {
            var d = (FinishedAt ?? DateTime.Now).Date;
            var today = DateTime.Now.Date;
            if (d == today) return "Bugün";
            if (d == today.AddDays(-1)) return "Dün";
            return d.ToString("d MMMM yyyy");
        }
    }

    /// <summary>Siralama anahtari: en yenisi ustte.</summary>
    public DateTime SortDate => FinishedAt ?? DateTime.Now;

    /// <summary>Arama kutusu ad ve siteye bakar.</summary>
    public bool Matches(string needle) =>
        needle.Length == 0
        || Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
        || Host.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private int _connections;
    public int Connections
    {
        get => _connections;
        set { if (Set(ref _connections, value)) Changed(nameof(PartsText)); }
    }

    /// <summary>Dosya turune gore Segoe Fluent Icons karakteri.</summary>
    public string IconGlyph =>
        Icons.ForFile(string.IsNullOrEmpty(FilePath) ? Name : FilePath);

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
        RowState.Active => Res("B.AccentSubtle", "#280078D4"),
        RowState.Failed => Res("B.DangerHover", "#28C42B1C"),
        _ => Res("B.HoverText", "#14808080"),
    };

    public Brush TagForeground => State switch
    {
        RowState.Active => Res("B.Accent", "#FF0078D4"),
        RowState.Failed => Res("B.Danger", "#FFC42B1C"),
        _ => Res("B.TextMuted", "#FF808080"),
    };

    public Brush BarBrush => State switch
    {
        RowState.Active => Res("B.Accent", "#FF0078D4"),
        RowState.Completed => Res("B.Success", "#FF0F7B0F"),
        RowState.Failed => Res("B.Danger", "#FFC42B1C"),
        _ => Res("B.TextFaint", "#FF808080"),
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
                if (FinishedAt is { } t) parts.Add(FinishedLabel(t));
                if (!string.IsNullOrEmpty(Note)) parts.Add(Note!);
                return string.Join("  ·  ", parts);
            }

            var meta = new List<string>();
            // Boyut henuz bilinmiyorsa "-" yazmak yerine alani hic gosterme.
            if (Total > 0) meta.Add($"{Format.Bytes(Completed)} / {Format.Bytes(Total)}");
            else if (Completed > 0) meta.Add(Format.Bytes(Completed));

            if (State == RowState.Active && Speed > 0) meta.Add(Format.Speed(Speed));
            if (State == RowState.Active && Eta is { } eta) meta.Add(Format.Duration(eta) + " kaldı");
            if (State != RowState.Active && !string.IsNullOrEmpty(Host)) meta.Add(Host);
            return string.Join("  ·  ", meta);
        }
    }

    /// <summary>"Dün 21:14" gibi; eski tarihlerde gun de yazilir.</summary>
    private static string FinishedLabel(DateTime t)
    {
        var d = t.Date;
        var today = DateTime.Now.Date;
        if (d == today) return t.ToString("HH:mm");
        if (d == today.AddDays(-1)) return "Dün " + t.ToString("HH:mm");
        return t.ToString("dd.MM.yyyy HH:mm");
    }

    public string Host
    {
        get
        {
            if (string.IsNullOrEmpty(SourceUri)) return "";
            return Uri.TryCreate(SourceUri, UriKind.Absolute, out var u) ? u.Host : "";
        }
    }

    public string SpeedText => State == RowState.Active && Speed > 0 ? Format.Speed(Speed) : "";

    public string EtaText => State == RowState.Active && Eta is { } e
        ? Format.Duration(e) + " kaldı" : "";

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
        Changed(nameof(SeparatorVisibility));
        Changed(nameof(HintText));
        Changed(nameof(HintVisibility));
        Changed(nameof(SpeedText));
        Changed(nameof(EtaText));
        Changed(nameof(PartsText));
    }

    private void RefreshAll()
    {
        RefreshProgress();
        Changed(nameof(TagText));
        Changed(nameof(KindShort));
        Changed(nameof(TagBackground));
        Changed(nameof(TagForeground));
        Changed(nameof(BarBrush));
        Changed(nameof(ToggleGlyph));
        Changed(nameof(ToggleVisibility));
        Changed(nameof(StatusText));
        Changed(nameof(StatusBrush));
        Changed(nameof(StatusVisibility));
        Changed(nameof(ProgressVisibility));
        Changed(nameof(RetryVisibility));
        Changed(nameof(OpenVisibility));
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
        Connections = item.Connections;
        UploadSpeed = item.UploadSpeed;
        Error = item.Status == Aria2Status.Error
            ? (string.IsNullOrWhiteSpace(item.ErrorMessage)
                ? $"aria2 hata kodu {item.ErrorCode}" : item.ErrorMessage)
            : null;
        if (!string.IsNullOrEmpty(item.FilePath)) FilePath = item.FilePath!;
        if (!string.IsNullOrEmpty(item.SourceUri)) SourceUri = item.SourceUri!;
        State = FromAria2(item.Status);

        // Durum degismeden de etiket degisebiliyor (indirme bitip paylasima
        // gecmek gibi), o yuzden her turda turetilmis alanlar tazeleniyor.
        RefreshAll();
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
