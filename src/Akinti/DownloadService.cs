using System.Net.Http;

namespace Akinti;

/// <summary>Indirme diyalogunda kullanicinin isaretledigi secenekler.</summary>
public sealed class DownloadRequest
{
    public string Url { get; set; } = "";
    public string FileName { get; set; } = "";
    public string TargetDir { get; set; } = "";
    public int Connections { get; set; } = 8;
    /// <summary>Bayt/sn, 0 = sinirsiz.</summary>
    public long SpeedLimitBytes { get; set; }
    /// <summary>true ise kuyruga duraklatilmis eklenir ("Sonra indir").</summary>
    public bool StartPaused { get; set; }

    // --- Indirme sonrasi (SADECE diyalogdan isaretlenebilir) ---
    public bool Extract { get; set; }
    public bool DeleteArchiveAfterExtract { get; set; }
    public bool RunAsAdmin { get; set; }
    public bool OpenFolder { get; set; }

    // --- Torrent ---
    /// <summary>Secili dosya indeksleri. Bos ise hepsi.</summary>
    public List<int> SelectedFileIndexes { get; set; } = new();
    public double? SeedRatio { get; set; }
}

public static class DownloadService
{
    /// <summary>
    /// Indirmeyi aria2'ye ekler ve indirme-sonrasi aksiyonlari GID'e bagli olarak kaydeder.
    /// GID doner.
    /// </summary>
    public static async Task<string> EnqueueAsync(Aria2Client client, Config cfg,
                                                  DownloadRequest req, CancellationToken ct = default)
    {
        if (!UrlHelper.IsSupportedScheme(req.Url))
            throw new InvalidOperationException("Desteklenmeyen adres: " + req.Url);

        Directory.CreateDirectory(req.TargetDir);

        var options = new Dictionary<string, string>
        {
            ["dir"] = req.TargetDir,
            ["max-connection-per-server"] = req.Connections.ToString(),
            ["split"] = req.Connections.ToString(),
        };

        if (req.SpeedLimitBytes > 0)
            options["max-download-limit"] = req.SpeedLimitBytes.ToString();

        if (req.StartPaused)
            options["pause"] = "true";

        var isTorrentSource = UrlHelper.IsMagnet(req.Url) || UrlHelper.IsTorrentFileUrl(req.Url);

        // "out" sadece tek dosyali normal indirmelerde anlamli; torrentte dosya
        // adlarini torrentin kendisi belirler.
        if (!isTorrentSource && !string.IsNullOrWhiteSpace(req.FileName))
            options["out"] = UrlHelper.Sanitize(req.FileName);

        if (req.SelectedFileIndexes.Count > 0)
            options["select-file"] = string.Join(',', req.SelectedFileIndexes);

        if (req.SeedRatio is { } ratio)
            options["seed-ratio"] = ratio.ToString(System.Globalization.CultureInfo.InvariantCulture);

        string gid;
        if (UrlHelper.IsTorrentFileUrl(req.Url) && !UrlHelper.IsMagnet(req.Url))
        {
            // .torrent dosyasini once biz cekip icerik olarak ekliyoruz; boylece
            // aria2 once torrenti indirip sonra icerigi indirmek yerine tek adimda basliyor.
            var bytes = await TryDownloadTorrentAsync(req.Url, ct).ConfigureAwait(false);
            gid = bytes is not null
                ? await client.AddTorrentAsync(bytes, options, ct).ConfigureAwait(false)
                : await client.AddUriAsync(req.Url, options, ct).ConfigureAwait(false);
        }
        else
        {
            gid = await client.AddUriAsync(req.Url, options, ct).ConfigureAwait(false);
        }

        PendingStore.Add(new PendingAction
        {
            Gid = gid,
            SourceUri = req.Url,
            Dir = req.TargetDir,
            FileName = req.FileName,
            Extract = req.Extract,
            DeleteArchiveAfterExtract = req.DeleteArchiveAfterExtract,
            RunAsAdmin = req.RunAsAdmin,
            OpenFolder = req.OpenFolder,
        });

        cfg.PushRecentFolder(req.TargetDir);
        cfg.Save();

        Log.Info($"Kuyruga eklendi [{gid}] {req.FileName} -> {req.TargetDir}");
        return gid;
    }

    private static async Task<byte[]?> TryDownloadTorrentAsync(string url, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 Akinti/1.0");
            var bytes = await http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
            // 10 MB ustu bir .torrent gercek degildir; RPC'ye base64 olarak sismesin.
            return bytes.Length is > 0 and < 10 * 1024 * 1024 ? bytes : null;
        }
        catch (Exception ex)
        {
            Log.Warn(".torrent dosyasi cekilemedi, adres dogrudan aria2'ye verilecek: " + ex.Message);
            return null;
        }
    }
}
