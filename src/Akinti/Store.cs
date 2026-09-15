using System.Text.Json;

namespace Akinti;

/// <summary>
/// Diyalogda secilen indirme-sonrasi aksiyonlar. aria2'ye verilemedigi icin
/// GID ile eslestirilip pending.json'da tutulur; kanca sureci oradan okur.
/// </summary>
public sealed class PendingAction
{
    public string Gid { get; set; } = "";
    public string SourceUri { get; set; } = "";
    public string Dir { get; set; } = "";
    public string FileName { get; set; } = "";
    public bool Extract { get; set; }
    public bool DeleteArchiveAfterExtract { get; set; }
    public bool RunAsAdmin { get; set; }
    public bool OpenFolder { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.Now;
}

public enum HistoryStatus { Completed, Failed, Cancelled }

public sealed class HistoryEntry
{
    public string Gid { get; set; } = "";
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string SourceUri { get; set; } = "";
    public long Size { get; set; }
    public HistoryStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public string? PostActionNote { get; set; }
    public DateTime FinishedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// Surecler arasi paylasilan kucuk JSON listeleri. GUI, kanca sureci ve native host
/// ayni dosyalara yazabildigi icin her islem adlandirilmis mutex altinda yapilir.
/// ponytail: kayit sayisi binleri gecip yavaslarsa SQLite'a gecilir.
/// </summary>
public static class JsonStore
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Dosyayi mutex altinda okur, degistirir, atomik yazar.</summary>
    public static void Mutate<T>(string path, Action<List<T>> change)
    {
        using var mutex = new Mutex(false, @"Local\Akinti.Store." + Path.GetFileName(path));
        var owned = false;
        try
        {
            owned = mutex.WaitOne(TimeSpan.FromSeconds(10));
            if (!owned) { Log.Warn($"{Path.GetFileName(path)} kilidi alinamadi, yazim atlandi."); return; }

            var list = ReadUnlocked<T>(path);
            change(list);

            AppPaths.EnsureDataDir();
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(list, Opts));
            File.Move(tmp, path, overwrite: true);
        }
        catch (AbandonedMutexException)
        {
            owned = true; // kilidi tutan surec cokmus; devam edilebilir
            Log.Warn($"{Path.GetFileName(path)} kilidi terk edilmisti.");
        }
        catch (Exception ex)
        {
            Log.Error($"{Path.GetFileName(path)} yazilamadi", ex);
        }
        finally
        {
            if (owned) mutex.ReleaseMutex();
        }
    }

    public static List<T> Read<T>(string path)
    {
        using var mutex = new Mutex(false, @"Local\Akinti.Store." + Path.GetFileName(path));
        var owned = false;
        try
        {
            owned = mutex.WaitOne(TimeSpan.FromSeconds(10));
            return ReadUnlocked<T>(path);
        }
        catch (AbandonedMutexException) { owned = true; return ReadUnlocked<T>(path); }
        catch (Exception ex) { Log.Error($"{Path.GetFileName(path)} okunamadi", ex); return []; }
        finally { if (owned) mutex.ReleaseMutex(); }
    }

    private static List<T> ReadUnlocked<T>(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text)) return [];
            return JsonSerializer.Deserialize<List<T>>(text, Opts) ?? [];
        }
        catch (JsonException ex)
        {
            Log.Warn($"{Path.GetFileName(path)} bozuk, sifirlaniyor: {ex.Message}");
            return [];
        }
    }
}

public static class PendingStore
{
    public static void Add(PendingAction action) =>
        JsonStore.Mutate<PendingAction>(AppPaths.PendingFile, list =>
        {
            list.RemoveAll(p => p.Gid == action.Gid);
            list.Add(action);
            // 7 gunden eski artiklari temizle (kanca hic calismamis olabilir)
            var cutoff = DateTime.Now.AddDays(-7);
            list.RemoveAll(p => p.AddedAt < cutoff);
        });

    /// <summary>Kaydi okur ve listeden cikarir. Kanca sureci bunu kullanir.</summary>
    public static PendingAction? Take(string gid)
    {
        PendingAction? found = null;
        JsonStore.Mutate<PendingAction>(AppPaths.PendingFile, list =>
        {
            found = list.FirstOrDefault(p => p.Gid == gid);
            if (found is not null) list.Remove(found);
        });
        return found;
    }

    public static PendingAction? Peek(string gid) =>
        JsonStore.Read<PendingAction>(AppPaths.PendingFile).FirstOrDefault(p => p.Gid == gid);
}

public static class HistoryStore
{
    private const int MaxEntries = 2000;

    public static void Add(HistoryEntry entry) =>
        JsonStore.Mutate<HistoryEntry>(AppPaths.HistoryFile, list =>
        {
            list.RemoveAll(h => h.Gid == entry.Gid);
            list.Insert(0, entry);
            if (list.Count > MaxEntries) list.RemoveRange(MaxEntries, list.Count - MaxEntries);
        });

    public static List<HistoryEntry> All() => JsonStore.Read<HistoryEntry>(AppPaths.HistoryFile);

    public static void Remove(string gid) =>
        JsonStore.Mutate<HistoryEntry>(AppPaths.HistoryFile, list => list.RemoveAll(h => h.Gid == gid));

    public static void Clear() =>
        JsonStore.Mutate<HistoryEntry>(AppPaths.HistoryFile, list => list.Clear());
}
