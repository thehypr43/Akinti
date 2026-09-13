namespace HafifDM;

/// <summary>
/// Tek dosyaya yazan minimal log. Hook ve native-host modlarinda pencere olmadigi
/// icin hatalarin gorulebilecegi tek yer burasi.
/// ponytail: dosya 1 MB'i gecince basa sariyor; donen dosya gerekirse sonra.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();

    public static void Info(string msg)  => Write("INFO", msg);
    public static void Warn(string msg)  => Write("WARN", msg);
    public static void Error(string msg) => Write("ERR ", msg);

    public static void Error(string msg, Exception ex) => Write("ERR ", $"{msg} :: {ex}");

    private static void Write(string level, string msg)
    {
        try
        {
            lock (Gate)
            {
                AppPaths.EnsureDataDir();
                var f = AppPaths.LogFile;
                if (File.Exists(f) && new FileInfo(f).Length > 1_000_000) File.Delete(f);
                File.AppendAllText(f, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}{Environment.NewLine}");
            }
        }
        catch { /* log yazamamak uygulamayi dusurmemeli */ }
    }
}
