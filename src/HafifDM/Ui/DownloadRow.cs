using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HafifDM.Ui;

public enum RowState { Active, Waiting, Paused, Completed, Failed, Cancelled }

/// <summary>Listede gorunen tek satir. Canli (aria2) ve gecmis (history.json) kayitlari birlestirir.</summary>
public sealed class DownloadRow : INotifyPropertyChanged
{
    public string Gid { get; init; } = "";

    private string _name = "";
    public string Name { get => _name; set => Set(ref _name, value); }

    private RowState _state;
    public RowState State
    {
        get => _state;
        set
        {
            if (Set(ref _state, value))
            {
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(CanPause));
                OnPropertyChanged(nameof(CanResume));
                OnPropertyChanged(nameof(CanCancel));
                OnPropertyChanged(nameof(CanRetry));
                OnPropertyChanged(nameof(IsFinished));
            }
        }
    }

    private long _total;
    public long Total { get => _total; set { if (Set(ref _total, value)) Refresh(); } }

    private long _completed;
    public long Completed { get => _completed; set { if (Set(ref _completed, value)) Refresh(); } }

    private long _speed;
    public long Speed { get => _speed; set { if (Set(ref _speed, value)) Refresh(); } }

    private long _uploadSpeed;
    public long UploadSpeed { get => _uploadSpeed; set { if (Set(ref _uploadSpeed, value)) Refresh(); } }

    private string? _error;
    public string? Error { get => _error; set { if (Set(ref _error, value)) OnPropertyChanged(nameof(SubText)); } }

    private string? _note;
    public string? Note { get => _note; set { if (Set(ref _note, value)) OnPropertyChanged(nameof(SubText)); } }

    public string FilePath { get; set; } = "";
    public string SourceUri { get; set; } = "";
    public string? InfoHash { get; set; }
    public bool IsTorrent { get; set; }
    public DateTime? FinishedAt { get; set; }

    // ---- Turetilmis gorunum alanlari ----

    public double Percent => Total > 0 ? Math.Clamp(Completed * 100.0 / Total, 0, 100) : 0;

    public string StateText => State switch
    {
        RowState.Active => "Indiriliyor",
        RowState.Waiting => "Sirada",
        RowState.Paused => "Duraklatildi",
        RowState.Completed => "Tamamlandi",
        RowState.Failed => "Basarisiz",
        RowState.Cancelled => "Iptal edildi",
        _ => "",
    };

    public string SubText
    {
        get
        {
            if (State == RowState.Failed)
                return "Basarisiz - " + (Error ?? "bilinmeyen hata");

            if (State == RowState.Completed)
            {
                var s = Format.Bytes(Total);
                if (FinishedAt is { } t) s += "  -  " + t.ToString("dd.MM.yyyy HH:mm");
                if (!string.IsNullOrEmpty(Note)) s += "  -  " + Note;
                return s;
            }

            if (State == RowState.Cancelled) return "Iptal edildi";

            var size = Total > 0
                ? $"{Format.Bytes(Completed)} / {Format.Bytes(Total)}"
                : Format.Bytes(Completed);

            if (State == RowState.Paused) return size + "  -  duraklatildi";
            if (State == RowState.Waiting) return size + "  -  sirada bekliyor";

            var parts = new List<string> { size, Format.Speed(Speed) };
            if (Eta is not null) parts.Add("kalan " + Format.Duration(Eta));
            if (IsTorrent && UploadSpeed > 0) parts.Add("gonderim " + Format.Speed(UploadSpeed));
            return string.Join("  -  ", parts);
        }
    }

    public TimeSpan? Eta =>
        Speed > 0 && Total > Completed ? TimeSpan.FromSeconds((Total - Completed) / (double)Speed) : null;

    public bool IsFinished => State is RowState.Completed or RowState.Failed or RowState.Cancelled;
    public bool CanPause => State is RowState.Active or RowState.Waiting;
    public bool CanResume => State == RowState.Paused;
    public bool CanCancel => !IsFinished;
    public bool CanRetry => State is RowState.Failed or RowState.Cancelled;

    private void Refresh()
    {
        OnPropertyChanged(nameof(Percent));
        OnPropertyChanged(nameof(SubText));
        OnPropertyChanged(nameof(Eta));
    }

    // ---- Donusturuculer ----

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
        State = FromAria2(item.Status);
        Total = item.TotalLength;
        Completed = item.CompletedLength;
        Speed = item.DownloadSpeed;
        UploadSpeed = item.UploadSpeed;
        Error = item.Status == Aria2Status.Error
            ? (string.IsNullOrWhiteSpace(item.ErrorMessage)
                ? $"aria2 hata kodu {item.ErrorCode}" : item.ErrorMessage)
            : null;
        FilePath = item.FilePath ?? FilePath;
        SourceUri = item.SourceUri ?? SourceUri;
        InfoHash = item.InfoHash;
        IsTorrent = item.IsTorrent;
        return this;
    }

    public static DownloadRow From(HistoryEntry h) => new()
    {
        Gid = h.Gid,
        Name = h.Name,
        State = h.Status switch
        {
            HistoryStatus.Completed => RowState.Completed,
            HistoryStatus.Failed => RowState.Failed,
            _ => RowState.Cancelled,
        },
        Total = h.Size,
        Completed = h.Status == HistoryStatus.Completed ? h.Size : 0,
        Error = h.ErrorMessage,
        Note = h.PostActionNote,
        FilePath = h.FilePath,
        SourceUri = h.SourceUri,
        IsTorrent = h.IsTorrent,
        FinishedAt = h.FinishedAt,
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
