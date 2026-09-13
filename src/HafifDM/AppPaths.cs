namespace HafifDM;

/// <summary>Uygulamanin diskteki sabit yerleri. Tek kaynak burasi.</summary>
public static class AppPaths
{
    /// <summary>%APPDATA%\HafifDM — ayarlar, parola, kuyruk, gecmis.</summary>
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HafifDM");

    public static string ConfigFile  => Path.Combine(DataDir, "config.json");
    public static string PendingFile => Path.Combine(DataDir, "pending.json");
    public static string HistoryFile => Path.Combine(DataDir, "history.json");
    public static string SessionFile => Path.Combine(DataDir, "aria2.session");
    public static string LogFile     => Path.Combine(DataDir, "hafifdm.log");
    public static string StartVbs    => Path.Combine(DataDir, "start-engine.vbs");

    /// <summary>Calisan HafifDM.exe'nin tam yolu.</summary>
    public static string ExePath { get; } =
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "HafifDM.exe");

    /// <summary>Uygulama klasorune bitisik tools\aria2c.exe.</summary>
    public static string Aria2Exe => Path.Combine(AppContext.BaseDirectory, "tools", "aria2c.exe");

    public static void EnsureDataDir() => Directory.CreateDirectory(DataDir);
}
