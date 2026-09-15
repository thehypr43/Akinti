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

    // Dosya turune gore otomatik alt klasorleme BILEREK YOK: kullanici her seyin
    // dogrudan DownloadDir'e inmesini istedi. Klasor yalnizca diyalogdan elle
    // degistirilebilir.

    public List<string> RecentFolders { get; set; } = new();

    // --- Hiz / esZamanlilik ---
    public int MaxConcurrentDownloads { get; set; } = 3;
    public int ConnectionsPerDownload { get; set; } = 8;
    /// <summary>Bayt/sn. 0 = sinirsiz.</summary>
    public long GlobalSpeedLimitBytes { get; set; }

    // --- Arsivci ---
    /// <summary>Bos ise otomatik tespit edilir (bkz. Archivers).</summary>
    public string ArchiverPath { get; set; } = "";

    // --- Davranis ---
    /// <summary>
    /// Diyalogu atlayip dogrudan indirmeye baslama. VARSAYILAN KAPALI ve oyle kalmali:
    /// kullanicinin en buyuk sarti her indirmenin goz onunde onaylanmasi.
    /// </summary>
    public bool QuickDownloadNoPrompt { get; set; }

    public bool OpenFolderAfterDownloadDefault { get; set; }

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

    public void PushRecentFolder(string folder)
    {
        RecentFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        RecentFolders.Insert(0, folder);
        if (RecentFolders.Count > 10) RecentFolders.RemoveRange(10, RecentFolders.Count - 10);
    }

    public bool IsConfigured => RpcPort > 0 && !string.IsNullOrEmpty(RpcSecret);
}
