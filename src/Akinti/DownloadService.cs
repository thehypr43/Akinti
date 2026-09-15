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

    /// <summary>Tarayicidan gelen sayfa adresi. Sadece HTTP Referer basligi olarak
    /// kullanilir; bazi dosya siteleri onsuz 403 donuyor.</summary>
    public string? Referrer { get; set; }

    /// <summary>Tarayicinin o adres icin tuttugu cerezler ("ad=deger; ...").
    /// Gofile gibi siteler dosyayi yalnizca oturum cerezine karsilik veriyor.</summary>
    public string? Cookie { get; set; }

    /// <summary>Tarayicinin kimlik dizesi; bazi siteler aria2'nin kendi UA'sini reddediyor.</summary>
    public string? UserAgent { get; set; }

    // --- Indirme sonrasi (SADECE diyalogdan isaretlenebilir) ---
    public bool Extract { get; set; }
    public bool DeleteArchiveAfterExtract { get; set; }
    public bool RunAsAdmin { get; set; }
    public bool OpenFolder { get; set; }
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

        var options = new Dictionary<string, object>
        {
            ["dir"] = req.TargetDir,
            ["max-connection-per-server"] = req.Connections.ToString(),
            ["split"] = req.Connections.ToString(),
        };

        // aria2'nin "header" secenegi coklu; RPC'ye dizi olarak verilir.
        var headers = new List<string>();
        if (!string.IsNullOrWhiteSpace(req.Cookie)) headers.Add("Cookie: " + req.Cookie);
        if (!string.IsNullOrWhiteSpace(req.UserAgent)) options["user-agent"] = req.UserAgent!;
        if (headers.Count > 0) options["header"] = headers.ToArray();

        if (req.SpeedLimitBytes > 0)
            options["max-download-limit"] = req.SpeedLimitBytes.ToString();

        if (req.StartPaused)
            options["pause"] = "true";

        if (!string.IsNullOrWhiteSpace(req.Referrer))
            options["referer"] = req.Referrer!;

        if (!string.IsNullOrWhiteSpace(req.FileName))
            options["out"] = UrlHelper.Sanitize(req.FileName);

        var gid = await client.AddUriAsync(req.Url, options, ct).ConfigureAwait(false);

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
}
