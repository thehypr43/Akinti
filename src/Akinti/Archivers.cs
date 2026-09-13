using System.Diagnostics;
using Microsoft.Win32;

namespace Akinti;

public enum ArchiverKind { SevenZip, NanaZip, UnRar, WinRar }

public sealed class Archiver
{
    public ArchiverKind Kind { get; init; }
    public string Path { get; init; } = "";
    public string DisplayName { get; init; } = "";

    /// <summary>UnRAR sadece .rar acar; digerleri her seyi acar.</summary>
    public bool Handles(string archivePath) =>
        Kind != ArchiverKind.UnRar ||
        archivePath.EndsWith(".rar", StringComparison.OrdinalIgnoreCase);
}

public sealed class ExtractResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public string? TargetDir { get; init; }

    /// <summary>Arsiv sifreli ve verilen sifre yok ya da yanlis. Cagiran taraf
    /// kullaniciya sorup ayni cagriyi sifreyle tekrarlar.</summary>
    public bool NeedsPassword { get; init; }
}

/// <summary>
/// Kurulu arsivcilerin tespiti ve cagrilmasi. 7-Zip / NanaZip / WinRAR desteklenir.
/// Hepsi konsol uygulamasi olarak, CreateNoWindow ile pencere gostermeden calistirilir;
/// tek istisna WinRAR.exe (GUI) ve o da sadece UnRAR'in acamadigi formatlar icin secilir.
/// </summary>
public static class Archivers
{
    private static readonly string[] ArchiveExtensions =
    [
        ".zip", ".rar", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".tbz2",
        ".xz", ".txz", ".zst", ".lzma", ".cab", ".arj", ".lzh", ".iso"
    ];

    public static bool IsArchive(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(ext)
               && ArchiveExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Bulunan tum arsivciler, tercih sirasina gore.</summary>
    public static List<Archiver> DetectAll()
    {
        var found = new List<Archiver>();

        void TryAdd(ArchiverKind kind, string? path, string name)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            if (found.Any(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase))) return;
            found.Add(new Archiver { Kind = kind, Path = path, DisplayName = name });
        }

        // 1) 7-Zip
        TryAdd(ArchiverKind.SevenZip, RegistryPath(@"SOFTWARE\7-Zip", "Path", "7z.exe"), "7-Zip");
        TryAdd(ArchiverKind.SevenZip,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
            "7-Zip");
        TryAdd(ArchiverKind.SevenZip, FromPathVar("7z.exe"), "7-Zip");

        // 2) NanaZip (7z ile ayni komut sozdizimi)
        TryAdd(ArchiverKind.NanaZip, FromPathVar("NanaZipC.exe"), "NanaZip");
        TryAdd(ArchiverKind.NanaZip,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "Microsoft", "WindowsApps", "NanaZipC.exe"),
            "NanaZip");

        // 3) WinRAR — UnRAR.exe once, cunku konsol uygulamasi ve hicbir pencere/tepsi gostermez
        var winrarDir = RegistryDir(@"SOFTWARE\WinRAR", "exe64")
                        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinRAR");
        TryAdd(ArchiverKind.UnRar, Path.Combine(winrarDir, "UnRAR.exe"), "UnRAR");
        TryAdd(ArchiverKind.WinRar, Path.Combine(winrarDir, "WinRAR.exe"), "WinRAR");

        return found;
    }

    /// <summary>
    /// Bu arsiv icin en uygun arsivci. Config'te elle bir yol verilmisse o kazanir.
    /// Hicbiri yoksa null.
    /// </summary>
    public static Archiver? Pick(string archivePath, Config cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg.ArchiverPath) && File.Exists(cfg.ArchiverPath))
        {
            var name = Path.GetFileName(cfg.ArchiverPath);
            var kind = name.ToLowerInvariant() switch
            {
                "unrar.exe" => ArchiverKind.UnRar,
                "winrar.exe" => ArchiverKind.WinRar,
                "nanazipc.exe" => ArchiverKind.NanaZip,
                _ => ArchiverKind.SevenZip,
            };
            var manual = new Archiver { Kind = kind, Path = cfg.ArchiverPath, DisplayName = name };
            if (manual.Handles(archivePath)) return manual;
        }

        return DetectAll().FirstOrDefault(a => a.Handles(archivePath));
    }

    public static bool AnyAvailable() => DetectAll().Count > 0;

    /// <summary>
    /// Arsivi, arsiv adini tasiyan bir alt klasore acar.
    /// Cakisma olursa klasor adina " (2)", " (3)" eklenir; var olan dosyanin uzerine yazilmaz.
    /// </summary>
    /// <param name="password">Sifreli arsivler icin. null ise arsivciye "sifre sorma"
    /// denir: aksi halde UnRAR stdin'de, WinRAR ise gorunmez bir pencerede sifre
    /// bekleyip surekli asili kalirdi.</param>
    public static async Task<ExtractResult> ExtractAsync(string archivePath, Config cfg,
                                                         string? password = null,
                                                         CancellationToken ct = default)
    {
        if (!File.Exists(archivePath))
            return new ExtractResult { Success = false, Message = "Arsiv bulunamadi: " + archivePath };

        var archiver = Pick(archivePath, cfg);
        if (archiver is null)
            return new ExtractResult
            {
                Success = false,
                Message = "Kurulu bir arsivci bulunamadi (7-Zip, NanaZip veya WinRAR gerekiyor)."
            };

        var parent = Path.GetDirectoryName(archivePath) ?? cfg.DownloadDir;
        var stem = Path.GetFileNameWithoutExtension(archivePath);
        // .tar.gz gibi cift uzantilarda ".tar" da atilsin
        if (stem.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) stem = stem[..^4];

        var target = Path.Combine(parent, UrlHelper.Sanitize(stem));
        for (var i = 2; Directory.Exists(target) && i < 100; i++)
            target = Path.Combine(parent, UrlHelper.Sanitize(stem) + $" ({i})");

        Directory.CreateDirectory(target);

        var psi = new ProcessStartInfo
        {
            FileName = archiver.Path,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = archiver.Kind != ArchiverKind.WinRar,
            RedirectStandardError = archiver.Kind != ArchiverKind.WinRar,
            WorkingDirectory = parent,
        };

        switch (archiver.Kind)
        {
            case ArchiverKind.SevenZip:
            case ArchiverKind.NanaZip:
                psi.ArgumentList.Add("x");
                psi.ArgumentList.Add("-o" + target);
                psi.ArgumentList.Add("-y");
                // -p her zaman veriliyor: bos parola bile olsa 7z artik sormuyor.
                psi.ArgumentList.Add("-p" + (password ?? ""));
                psi.ArgumentList.Add("-bso0");   // stdout sus
                psi.ArgumentList.Add("--");
                psi.ArgumentList.Add(archivePath);
                break;

            case ArchiverKind.UnRar:
                psi.ArgumentList.Add("x");
                psi.ArgumentList.Add("-o+");     // uzerine yaz (hedef zaten bos klasor)
                psi.ArgumentList.Add("-y");
                psi.ArgumentList.Add("-idq");    // sessiz
                psi.ArgumentList.Add(password is null ? "-p-" : "-p" + password);
                psi.ArgumentList.Add(archivePath);
                psi.ArgumentList.Add(target + Path.DirectorySeparatorChar);
                break;

            case ArchiverKind.WinRar:
                psi.ArgumentList.Add("x");
                psi.ArgumentList.Add("-o+");
                psi.ArgumentList.Add("-y");
                psi.ArgumentList.Add("-ibck");   // arka planda, pencere acmadan
                psi.ArgumentList.Add(password is null ? "-p-" : "-p" + password);
                psi.ArgumentList.Add(archivePath);
                psi.ArgumentList.Add(target + Path.DirectorySeparatorChar);
                break;
        }

        try
        {
            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Arsivci baslatilamadi.");

            var stderr = psi.RedirectStandardError
                ? await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false)
                : "";
            if (psi.RedirectStandardOutput) _ = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);

            await proc.WaitForExitAsync(ct).ConfigureAwait(false);

            // 0 = tamam, 1 = uyari (dosyalar yine de acildi)
            if (proc.ExitCode is 0 or 1)
            {
                Log.Info($"Arsiv acildi ({archiver.DisplayName}): {archivePath} -> {target}");
                return new ExtractResult
                {
                    Success = true,
                    TargetDir = target,
                    Message = $"{archiver.DisplayName} ile acildi: {target}"
                };
            }

            TryRemoveEmptyDir(target);

            if (NeedsPassword(archiver.Kind, proc.ExitCode, stderr))
                return new ExtractResult
                {
                    Success = false,
                    NeedsPassword = true,
                    Message = password is null ? "Arşiv şifreli." : "Şifre yanlış."
                };

            var msg = $"{archiver.DisplayName} hata verdi (kod {proc.ExitCode}). {stderr.Trim()}".Trim();
            Log.Error(msg);
            return new ExtractResult { Success = false, Message = msg };
        }
        catch (Exception ex)
        {
            TryRemoveEmptyDir(target);
            Log.Error("Arsiv acilamadi", ex);
            return new ExtractResult { Success = false, Message = "Arsiv acilamadi: " + ex.Message };
        }
    }

    /// <summary>Hatanin sebebi sifre mi? RAR araclari bunun icin ayri bir cikis
    /// kodu (11) veriyor; 7z genel bir hata kodu verip sebebi metne yaziyor.</summary>
    private static bool NeedsPassword(ArchiverKind kind, int exitCode, string stderr)
    {
        if (kind is ArchiverKind.UnRar or ArchiverKind.WinRar && exitCode == 11) return true;

        return stderr.Contains("Wrong password", StringComparison.OrdinalIgnoreCase)
               || stderr.Contains("encrypted", StringComparison.OrdinalIgnoreCase)
               || stderr.Contains("password", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryRemoveEmptyDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
        catch { /* onemsiz */ }
    }

    private static string? RegistryPath(string key, string valueName, string fileName)
    {
        var dir = RegistryDir(key, valueName);
        return dir is null ? null : Path.Combine(dir, fileName);
    }

    private static string? RegistryDir(string key, string valueName)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var k = hklm.OpenSubKey(key);
                if (k?.GetValue(valueName) is string v && !string.IsNullOrWhiteSpace(v))
                    return File.Exists(v) ? Path.GetDirectoryName(v) : v.TrimEnd('\\');
            }
            catch { /* anahtar yok */ }
        }
        return null;
    }

    private static string? FromPathVar(string exeName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), exeName);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* gecersiz PATH parcasi */ }
        }
        return null;
    }
}
