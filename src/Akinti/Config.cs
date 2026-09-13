using System.Text.Json;
using System.Text.Json.Serialization;

namespace Akinti;

/// <summary>
/// %APPDATA%\Akinti\config.json. RPC parolasini tuttugu icin dosyanin ACL'i
/// kuruluma ait kullaniciya kisitlidir (bkz. scripts\install.ps1).
/// </summary>
public sealed class Config
{
    // --- Motor baglantisi (install.ps1 uretir, elle degistirilmez) ---
    public int RpcPort { get; set; }
    public string RpcSecret { get; set; } = "";

    // --- Klasorler ---
    public string DownloadDir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    /// <summary>Uzanti -> alt klasor adi. Bos birakilan uzanti DownloadDir'e iner.</summary>
    public Dictionary<string, string> TypeRules { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [".zip"] = "Arsivler",  [".rar"] = "Arsivler",  [".7z"]  = "Arsivler",
        [".tar"] = "Arsivler",  [".gz"]  = "Arsivler",  [".xz"]  = "Arsivler",
        [".zst"] = "Arsivler",  [".bz2"] = "Arsivler",
        [".exe"] = "Programlar",[".msi"] = "Programlar",[".msix"]= "Programlar",
        [".mp4"] = "Video",     [".mkv"] = "Video",     [".avi"] = "Video",
        [".mov"] = "Video",     [".webm"]= "Video",
        [".mp3"] = "Muzik",     [".flac"]= "Muzik",     [".wav"] = "Muzik",
        [".m4a"] = "Muzik",     [".opus"]= "Muzik",
        [".pdf"] = "Belgeler",  [".docx"]= "Belgeler",  [".xlsx"]= "Belgeler",
        [".epub"]= "Belgeler",  [".pptx"]= "Belgeler",
        [".jpg"] = "Resimler",  [".jpeg"]= "Resimler",  [".png"] = "Resimler",
        [".gif"] = "Resimler",  [".webp"]= "Resimler",  [".avif"]= "Resimler",
        [".iso"] = "Imajlar",   [".img"] = "Imajlar",
        [".torrent"] = "Torrent",
    };

    public List<string> RecentFolders { get; set; } = new();

    // --- Hiz / esZamanlilik ---
    public int MaxConcurrentDownloads { get; set; } = 3;
    public int ConnectionsPerDownload { get; set; } = 8;
    /// <summary>Bayt/sn. 0 = sinirsiz.</summary>
    public long GlobalSpeedLimitBytes { get; set; }

    // --- Arsivci ---
    /// <summary>Bos ise otomatik tespit edilir (bkz. Archivers).</summary>
    public string ArchiverPath { get; set; } = "";

    // --- Torrent ---
    public int TorrentPort { get; set; } = 51413;
    public bool EnableDht { get; set; } = true;
    public double SeedRatio { get; set; } = 1.0;
    /// <summary>Dakika. 0 = oran dolunca dur.</summary>
    public int SeedTimeMinutes { get; set; }

    // --- Davranis ---
    /// <summary>
    /// Diyalogu atlayip dogrudan indirmeye baslama. VARSAYILAN KAPALI ve oyle kalmali:
    /// kullanicinin en buyuk sarti her indirmenin goz onunde onaylanmasi.
    /// </summary>
    public bool QuickDownloadNoPrompt { get; set; }

    public bool OpenFolderAfterDownloadDefault { get; set; }
    public bool ExtractArchivesDefault { get; set; }

    /// <summary>Guc kullanici kacis kapisi. Dogrudan aria2c komut satirina eklenir.</summary>
    public List<string> ExtraAria2Args { get; set; } = new();

    // ------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static Config Load()
    {
        try
        {
            if (File.Exists(AppPaths.ConfigFile))
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(AppPaths.ConfigFile), JsonOpts)
                       ?? new Config();
        }
        catch (Exception ex)
        {
            Log.Warn($"config.json okunamadi, varsayilanlar kullanilacak: {ex.Message}");
        }
        return new Config();
    }

    public void Save()
    {
        AppPaths.EnsureDataDir();
        // Atomik yazim: yarim kalmis bir yazim config'i (ve RPC parolasini) bozmasin.
        var tmp = AppPaths.ConfigFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOpts));
        File.Move(tmp, AppPaths.ConfigFile, overwrite: true);
    }

    /// <summary>Uzantiya gore hedef klasor. Kural yoksa DownloadDir.</summary>
    public string ResolveFolderFor(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (!string.IsNullOrEmpty(ext) && TypeRules.TryGetValue(ext, out var sub) && !string.IsNullOrWhiteSpace(sub))
            return Path.Combine(DownloadDir, sub);
        return DownloadDir;
    }

    public void PushRecentFolder(string folder)
    {
        RecentFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        RecentFolders.Insert(0, folder);
        if (RecentFolders.Count > 10) RecentFolders.RemoveRange(10, RecentFolders.Count - 10);
    }

    public bool IsConfigured => RpcPort > 0 && !string.IsNullOrEmpty(RpcSecret);
}
