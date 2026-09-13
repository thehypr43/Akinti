using System.Diagnostics;
using System.Text;

namespace HafifDM;

/// <summary>
/// aria2c.exe surecinin yonetimi: conf dosyasi uretimi, gizli baslatma, saglik bekleme.
///
/// Komut satiri argumanlari YERINE conf dosyasi kullaniliyor. Boylece motoru baslatan
/// iki ayri yol (Windows acilisindaki scheduled task -> start-engine.vbs ve buradaki
/// runtime baslatma) ayni tek kaynaktan besleniyor.
/// </summary>
public static class Aria2Engine
{
    public static string ConfFile => Path.Combine(AppPaths.DataDir, "aria2.conf");

    /// <summary>Config'ten aria2.conf uretir. Ayarlar degisince yeniden cagrilir.</summary>
    public static void WriteConfFile(Config cfg)
    {
        AppPaths.EnsureDataDir();
        var sb = new StringBuilder();

        void Set(string k, object v) => sb.Append(k).Append('=').Append(v).Append('\n');

        sb.Append("# HafifDM tarafindan uretildi. Elle yapilan degisiklikler\n");
        sb.Append("# ayarlar kaydedilince silinir. Kalici ek ayar icin\n");
        sb.Append("# config.json icindeki ExtraAria2Args alanini kullan.\n\n");

        // --- RPC: kilitli kapi ---
        Set("enable-rpc", "true");
        Set("rpc-listen-all", "false");          // sadece 127.0.0.1
        Set("rpc-listen-port", cfg.RpcPort);     // varsayilan 6800 degil, kurulumda rastgele
        Set("rpc-secret", cfg.RpcSecret);        // parolasiz istek reddedilir
        Set("rpc-allow-origin-all", "false");
        Set("rpc-save-upload-metadata", "true");

        // --- Kalicilik ---
        Set("dir", cfg.DownloadDir);
        Set("save-session", AppPaths.SessionFile);
        Set("save-session-interval", 30);
        Set("max-download-result", 1000);
        // force-save BILEREK kapali: acik oldugunda aria2, tamamlanan her indirmenin
        // yaninda .aria2 kontrol dosyasini birakiyor ve indirme klasoru cop doluyor.
        // Bedeli: seed edilen bir torrent, makine yeniden baslayinca seed'e devam etmez.
        if (File.Exists(AppPaths.SessionFile))
            Set("input-file", AppPaths.SessionFile);

        // --- Indirme davranisi ---
        Set("continue", "true");
        Set("auto-file-renaming", "true");
        Set("allow-overwrite", "false");
        Set("check-certificate", "true");
        Set("remote-time", "true");
        Set("disk-cache", "32M");
        Set("file-allocation", "falloc");
        Set("max-concurrent-downloads", cfg.MaxConcurrentDownloads);
        Set("max-connection-per-server", cfg.ConnectionsPerDownload);
        Set("split", cfg.ConnectionsPerDownload);
        Set("min-split-size", "1M");
        Set("max-tries", 5);
        Set("retry-wait", 3);
        if (cfg.GlobalSpeedLimitBytes > 0)
            Set("max-overall-download-limit", cfg.GlobalSpeedLimitBytes);

        // --- Torrent ---
        Set("listen-port", cfg.TorrentPort);
        Set("dht-listen-port", cfg.TorrentPort);
        Set("enable-dht", cfg.EnableDht ? "true" : "false");
        Set("enable-peer-exchange", "true");
        Set("bt-enable-lpd", "true");
        Set("seed-ratio", cfg.SeedRatio.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Set("seed-time", cfg.SeedTimeMinutes);
        Set("bt-save-metadata", "true");
        Set("follow-torrent", "true");

        // --- Indirme sonrasi kanca ---
        // Ucu de ayni exe'yi cagirir; hangi olayin dustugunu exe tellStatus ile ogrenir.
        // HafifDM.exe GUI-subsystem oldugu icin konsol penceresi acilmaz.
        Set("on-download-complete", AppPaths.ExePath);
        Set("on-bt-download-complete", AppPaths.ExePath);
        Set("on-download-error", AppPaths.ExePath);

        foreach (var extra in cfg.ExtraAria2Args)
        {
            var line = extra.TrimStart('-');
            if (line.Length > 0) sb.Append(line).Append('\n');
        }

        File.WriteAllText(ConfFile, sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>Calisan aria2c surecleri (bizim conf ile baslatilmis olanlar dahil).</summary>
    public static bool IsProcessRunning() =>
        Process.GetProcessesByName("aria2c").Length > 0;

    /// <summary>
    /// Motor ayakta degilse gizli baslatir ve RPC cevap verene kadar bekler.
    /// Zaten ayaktaysa hicbir sey yapmaz. true = RPC hazir.
    /// </summary>
    public static async Task<bool> EnsureRunningAsync(Config cfg, CancellationToken ct = default)
    {
        using (var probe = new Aria2Client(cfg))
        {
            if (await probe.GetVersionAsync(ct).ConfigureAwait(false) is not null)
                return true;
        }

        if (!File.Exists(AppPaths.Aria2Exe))
        {
            Log.Error($"aria2c.exe bulunamadi: {AppPaths.Aria2Exe}");
            return false;
        }

        WriteConfFile(cfg);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = AppPaths.Aria2Exe,
                UseShellExecute = false,
                CreateNoWindow = true,          // konsol penceresi yanip sonmez
                WorkingDirectory = AppPaths.DataDir,
            };
            psi.ArgumentList.Add("--conf-path=" + ConfFile);
            Process.Start(psi);
            Log.Info("aria2c baslatildi.");
        }
        catch (Exception ex)
        {
            Log.Error("aria2c baslatilamadi", ex);
            return false;
        }

        // RPC ayaga kalkmasi icin en fazla 10 sn bekle.
        using var client = new Aria2Client(cfg);
        for (var i = 0; i < 40; i++)
        {
            await Task.Delay(250, ct).ConfigureAwait(false);
            if (await client.GetVersionAsync(ct).ConfigureAwait(false) is not null)
                return true;
        }

        Log.Error("aria2c basladi ama RPC 10 sn icinde cevap vermedi.");
        return false;
    }
}
