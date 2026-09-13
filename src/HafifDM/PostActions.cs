using System.ComponentModel;
using System.Diagnostics;

namespace HafifDM;

/// <summary>
/// aria2'nin indirme-sonrasi kancasi. Uc olay (complete / bt-complete / error) ayni
/// exe'yi cagirir; hangisinin dustugunu tellStatus ile ogreniriz.
///
/// Bu surecin penceresi yoktur. Hata olursa kullaniciya bir sey gosterilmez, log'a yazilir.
/// </summary>
public static class PostActions
{
    private static readonly string[] RunnableExtensions = [".exe", ".msi", ".msix", ".bat", ".cmd", ".ps1"];

    /// <summary>aria2 GID'i 16 haneli hex'tir. Kanca modunu bundan taniyoruz.</summary>
    public static bool LooksLikeGid(string s) =>
        s.Length == 16 && s.All(Uri.IsHexDigit);

    public static async Task HandleAsync(string gid, string? reportedPath)
    {
        var cfg = Config.Load();
        var pending = PendingStore.Take(gid);

        DownloadItem? status = null;
        if (cfg.IsConfigured)
        {
            using var client = new Aria2Client(cfg);
            status = await client.TellStatusAsync(gid).ConfigureAwait(false);
        }

        var filePath = FirstNonEmpty(status?.FilePath, reportedPath);
        var name = status?.DisplayName
                   ?? (string.IsNullOrEmpty(filePath) ? gid : Path.GetFileName(filePath!));

        // --- Basarisiz / iptal ---
        if (status is { Status: Aria2Status.Error })
        {
            HistoryStore.Add(new HistoryEntry
            {
                Gid = gid,
                Name = name,
                FilePath = filePath ?? "",
                SourceUri = pending?.SourceUri ?? status.SourceUri ?? "",
                Size = status.TotalLength,
                Status = HistoryStatus.Failed,
                ErrorMessage = string.IsNullOrWhiteSpace(status.ErrorMessage)
                    ? $"aria2 hata kodu {status.ErrorCode}"
                    : status.ErrorMessage,
                IsTorrent = status.IsTorrent,
            });
            Log.Warn($"Indirme basarisiz: {name} ({status.ErrorMessage})");
            return;
        }

        if (status is { Status: Aria2Status.Removed })
        {
            HistoryStore.Add(new HistoryEntry
            {
                Gid = gid, Name = name, FilePath = filePath ?? "",
                SourceUri = pending?.SourceUri ?? "", Size = status.TotalLength,
                Status = HistoryStatus.Cancelled, IsTorrent = status.IsTorrent,
            });
            return;
        }

        // --- Tamamlandi ---
        var notes = new List<string>();

        if (pending is not null && !string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            if (pending.Extract && Archivers.IsArchive(filePath!))
                notes.Add(await ExtractAsync(filePath!, pending, cfg).ConfigureAwait(false));

            if (pending.RunAsAdmin)
                notes.Add(RunAsAdmin(filePath!));
        }
        else if (pending is not null && !string.IsNullOrEmpty(filePath))
        {
            Log.Warn($"Indirme sonrasi dosya bulunamadi, aksiyonlar atlandi: {filePath}");
        }

        if (pending?.OpenFolder == true && !string.IsNullOrEmpty(filePath))
            OpenFolder(filePath!);

        HistoryStore.Add(new HistoryEntry
        {
            Gid = gid,
            Name = name,
            FilePath = filePath ?? "",
            SourceUri = pending?.SourceUri ?? status?.SourceUri ?? "",
            Size = status?.TotalLength ?? SafeFileLength(filePath),
            Status = HistoryStatus.Completed,
            PostActionNote = notes.Count > 0 ? string.Join(" | ", notes) : null,
            IsTorrent = status?.IsTorrent ?? false,
        });

        Log.Info($"Tamamlandi: {name}{(notes.Count > 0 ? " :: " + string.Join(" | ", notes) : "")}");
    }

    // ---------------- Tekil aksiyonlar ----------------

    private static async Task<string> ExtractAsync(string filePath, PendingAction pending, Config cfg)
    {
        var result = await Archivers.ExtractAsync(filePath, cfg).ConfigureAwait(false);
        if (!result.Success) return "Cikarma basarisiz: " + result.Message;

        if (pending.DeleteArchiveAfterExtract)
        {
            try
            {
                File.Delete(filePath);
                return result.Message + " (arsiv silindi)";
            }
            catch (Exception ex)
            {
                Log.Warn($"Arsiv silinemedi: {ex.Message}");
                return result.Message + " (arsiv silinemedi)";
            }
        }
        return result.Message;
    }

    /// <summary>
    /// Indirilen dosyayi yonetici olarak calistirir.
    /// Bu bayrak SADECE indirme diyalogundan isaretlenebilir; eklentiden gelen
    /// mesajdan hicbir zaman okunmaz (bkz. NativeHost).
    /// </summary>
    public static string RunAsAdmin(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (!RunnableExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return $"Yonetici olarak calistirma atlandi: {ext} calistirilabilir bir tur degil.";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(filePath) ?? "",
            };

            // .ps1 dogrudan calistirilamaz, PowerShell uzerinden acilir
            if (ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = "powershell.exe";
                psi.Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{filePath}\"";
            }

            Process.Start(psi);
            return "Yonetici olarak calistirildi.";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // Kullanici UAC istemini reddetti. Bu bir hata degil, hata penceresi gosterilmez.
            Log.Info("UAC istemi reddedildi, calistirma iptal.");
            return "Yonetici izni verilmedi, calistirilmadi.";
        }
        catch (Exception ex)
        {
            Log.Error("Yonetici olarak calistirilamadi", ex);
            return "Calistirilamadi: " + ex.Message;
        }
    }

    public static void OpenFolder(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"")
                    { UseShellExecute = true });
            else
            {
                var dir = Directory.Exists(filePath) ? filePath : Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Klasor acilamadi: " + ex.Message);
        }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static long SafeFileLength(string? path)
    {
        try { return !string.IsNullOrEmpty(path) && File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }
}
