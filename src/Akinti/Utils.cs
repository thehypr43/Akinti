using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Akinti;

public static class UrlHelper
{
    /// <summary>Yalnizca indirilebilir sema. javascript:, data:, file:, magnet:
    /// gibi semalar reddedilir - torrent destegi yok.</summary>
    public static bool IsSupportedScheme(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        return u.Scheme is "http" or "https" or "ftp" or "ftps" or "sftp";
    }

    /// <summary>URL'den dosya adi tahmini. Sorgu dizesi ve yuzde kodlama temizlenir.</summary>
    public static string GuessFileName(string url)
    {
        try
        {
            var path = Uri.TryCreate(url, UriKind.Absolute, out var u)
                ? u.AbsolutePath
                : url.Split('?', '#')[0];

            var last = path.TrimEnd('/').Split('/').LastOrDefault();
            if (string.IsNullOrWhiteSpace(last)) return "indirilen-dosya";
            return Sanitize(Uri.UnescapeDataString(last));
        }
        catch
        {
            return "indirilen-dosya";
        }
    }

    /// <summary>
    /// Windows icin guvenli dosya adi. Yol ayiraci ve ".." temizlenir, boylece
    /// sunucudan gelen bir ad hedef klasorun disina yazamaz.
    /// </summary>
    public static string Sanitize(string name)
    {
        name = name.Replace('\\', '_').Replace('/', '_');
        var sb = new StringBuilder(name.Length);
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);

        var result = sb.ToString().Trim().Trim('.');
        if (result.Length == 0) return "indirilen-dosya";
        if (result.Length > 200) result = result[..200];

        // Windows'ta ayrilmis adlar
        var stem = Path.GetFileNameWithoutExtension(result);
        string[] reserved = ["CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6",
                             "COM7","COM8","COM9","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6",
                             "LPT7","LPT8","LPT9"];
        if (reserved.Contains(stem, StringComparer.OrdinalIgnoreCase)) result = "_" + result;

        return result;
    }
}

public sealed class RemoteFileInfo
{
    public string FileName { get; init; } = "";
    public long Size { get; init; }
    public bool Resumable { get; init; }
    public string? ContentType { get; init; }
}

public static class RemoteProbe
{
    /// <summary>
    /// Yoklamanin tarayicidan devraldigi kimlik. Cerez ve Referer olmadan bircok
    /// site dosya yerine giris sayfasi donuyor; o zaman Content-Disposition da
    /// gelmiyor ve ad adresteki rastgele belirtece dusuyordu.
    /// </summary>
    public sealed class Context
    {
        public string? Cookie { get; init; }
        public string? Referrer { get; init; }
        public string? UserAgent { get; init; }
    }

    private static readonly Dictionary<string, string> ExtByType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/zip"] = ".zip",
        ["application/x-zip-compressed"] = ".zip",
        ["application/x-rar-compressed"] = ".rar",
        ["application/vnd.rar"] = ".rar",
        ["application/x-7z-compressed"] = ".7z",
        ["application/gzip"] = ".gz",
        ["application/x-msdownload"] = ".exe",
        ["application/x-msi"] = ".msi",
        ["application/vnd.microsoft.portable-executable"] = ".exe",
        ["application/pdf"] = ".pdf",
        ["application/epub+zip"] = ".epub",
        ["application/json"] = ".json",
        ["video/mp4"] = ".mp4",
        ["video/x-matroska"] = ".mkv",
        ["video/webm"] = ".webm",
        ["audio/mpeg"] = ".mp3",
        ["audio/flac"] = ".flac",
        ["audio/ogg"] = ".ogg",
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp",
        ["text/plain"] = ".txt",
    };

    /// <summary>
    /// Diyalogda dosya adi ve boyutu gosterebilmek icin hafif bir yoklama.
    /// Basarisiz olursa URL tahminine duser, asla istisna firlatmaz.
    /// </summary>
    public static async Task<RemoteFileInfo> ProbeAsync(string url, Context? ctx = null,
                                                        CancellationToken ct = default)
    {
        var fallback = new RemoteFileInfo { FileName = UrlHelper.GuessFileName(url) };
        if (!UrlHelper.IsSupportedScheme(url)) return fallback;

        try
        {
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                UseCookies = false,   // cerez basligini biz koyuyoruz
            };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(
                string.IsNullOrWhiteSpace(ctx?.UserAgent) ? "Mozilla/5.0 Akinti/1.0" : ctx!.UserAgent!);
            if (!string.IsNullOrWhiteSpace(ctx?.Cookie))
                http.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", ctx!.Cookie);
            if (!string.IsNullOrWhiteSpace(ctx?.Referrer))
                http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", ctx!.Referrer);

            var result = await TryAsync(http, HttpMethod.Head, url, ct).ConfigureAwait(false);

            // Bazi sunucular HEAD'i hic kabul etmiyor (405) ya da bos donuyor.
            // Tek baytlik bir GET ayni basliklari verir, indirmeyi de baslatmaz.
            if (result is null || (result.Size <= 0 && !Path.HasExtension(result.FileName)))
            {
                var ranged = await TryAsync(http, HttpMethod.Get, url, ct, rangeOnly: true).ConfigureAwait(false);
                if (ranged is not null) result = ranged;
            }

            return result ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static async Task<RemoteFileInfo?> TryAsync(HttpClient http, HttpMethod method, string url,
                                                        CancellationToken ct, bool rangeOnly = false)
    {
        try
        {
            using var req = new HttpRequestMessage(method, url);
            if (rangeOnly) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);

            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                                       .ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var cd = resp.Content.Headers.ContentDisposition;
            var name = cd?.FileNameStar ?? cd?.FileName;
            name = string.IsNullOrWhiteSpace(name)
                ? UrlHelper.GuessFileName(resp.RequestMessage?.RequestUri?.ToString() ?? url)
                : UrlHelper.Sanitize(name.Trim('"'));

            var type = resp.Content.Headers.ContentType?.MediaType;

            // Tek baytlik istekte ContentLength 1 olur; gercek boyut Content-Range'de.
            var size = resp.Content.Headers.ContentLength ?? 0;
            var cr = resp.Content.Headers.ContentRange;
            if (cr?.Length is { } total && total > 0) size = total;

            return new RemoteFileInfo
            {
                FileName = WithExtension(name, type),
                Size = size,
                Resumable = resp.Headers.AcceptRanges.Contains("bytes") || cr is not null,
                ContentType = type,
            };
        }
        catch { return null; }
    }

    /// <summary>
    /// Adres bir belirtecse (".../8f3a91c2b4") ad uzantisiz kaliyor ve dosya
    /// Windows'ta hicbir programla acilmiyordu. Sunucunun bildirdigi turden
    /// dogru uzantiyi ekler.
    /// </summary>
    private static string WithExtension(string name, string? contentType)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        if (Path.HasExtension(name)) return name;
        if (contentType is null || !ExtByType.TryGetValue(contentType, out var ext)) return name;
        return name + ext;
    }
}

/// <summary>
/// Segoe Fluent Icons karakterleri. Windows 11'de kurulu geldigi icin
/// ayrica bir ikon fontu tasimiyoruz.
/// </summary>
public static class Icons
{
    public static string ForFile(string nameOrPath, bool isTorrent = false)
    {
        if (isTorrent) return "\uE896";
        return Path.GetExtension(nameOrPath).ToLowerInvariant() switch
        {
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".wmv" => "\uE714",
            ".mp3" or ".flac" or ".wav" or ".m4a" or ".opus" or ".ogg" => "\uE8D6",
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".avif" or ".bmp" => "\uEB9F",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".xz" or ".zst" or ".bz2" => "\uF012",
            ".exe" or ".msi" or ".msix" => "\uE74C",
            ".bat" or ".cmd" or ".ps1" => "\uE756",
            ".iso" or ".img" => "\uE958",
            ".torrent" => "\uE896",
            ".pdf" or ".docx" or ".xlsx" or ".pptx" or ".epub" or ".txt" => "\uE8A5",
            _ => "\uE7C3",
        };
    }

    /// <summary>Dosyanin ne oldugunu tek kelimeyle soyler: "Uygulama", "Arsiv"...
    /// Bitmis indirme penceresinde boyutun yanina yaziliyor.</summary>
    public static string KindOf(string nameOrPath)
    {
        return Path.GetExtension(nameOrPath).ToLowerInvariant() switch
        {
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".wmv" => "Video",
            ".mp3" or ".flac" or ".wav" or ".m4a" or ".opus" or ".ogg" => "Ses",
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".avif" or ".bmp" => "Resim",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".xz" or ".zst" or ".bz2" => "Arşiv",
            ".exe" or ".msi" or ".msix" => "Uygulama",
            ".bat" or ".cmd" or ".ps1" => "Betik",
            ".iso" or ".img" => "Disk imajı",
            ".pdf" or ".docx" or ".xlsx" or ".pptx" or ".epub" or ".txt" => "Belge",
            _ => "Dosya",
        };
    }
}

public static class Format
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Bytes(long bytes)
    {
        if (bytes <= 0) return "-";
        double v = bytes;
        var u = 0;
        while (v >= 1024 && u < Units.Length - 1) { v /= 1024; u++; }
        return string.Create(CultureInfo.CurrentCulture, $"{v:0.#} {Units[u]}");
    }

    public static string Speed(long bytesPerSecond) =>
        bytesPerSecond <= 0 ? "-" : Bytes(bytesPerSecond) + "/sn";

    public static string Duration(TimeSpan? span)
    {
        if (span is not { } t || t.TotalSeconds < 1) return "-";
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays} gun {t.Hours} sa";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} sa {t.Minutes} dk";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes} dk {t.Seconds} sn";
        return $"{(int)t.TotalSeconds} sn";
    }

    public static string StatusText(Aria2Status s) => s switch
    {
        Aria2Status.Active => "Indiriliyor",
        Aria2Status.Waiting => "Sirada",
        Aria2Status.Paused => "Duraklatildi",
        Aria2Status.Complete => "Tamamlandi",
        Aria2Status.Error => "Basarisiz",
        Aria2Status.Removed => "Iptal edildi",
        _ => "Bilinmiyor",
    };
}
