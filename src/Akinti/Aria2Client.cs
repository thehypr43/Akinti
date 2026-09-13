using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Akinti;

public enum Aria2Status { Unknown, Active, Waiting, Paused, Error, Complete, Removed }

/// <summary>aria2'den donen tek indirme kaydinin bize lazim olan alanlari.</summary>
public sealed class DownloadItem
{
    public string Gid { get; init; } = "";
    public Aria2Status Status { get; init; }
    public long TotalLength { get; init; }
    public long CompletedLength { get; init; }
    public long DownloadSpeed { get; init; }
    public long UploadSpeed { get; init; }
    public int ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string Dir { get; init; } = "";
    public string? FilePath { get; init; }
    public string? SourceUri { get; init; }
    public string? InfoHash { get; init; }
    public string? TorrentName { get; init; }
    public int NumSeeders { get; init; }

    public bool IsTorrent => !string.IsNullOrEmpty(InfoHash);

    /// <summary>Torrent ise torrent adi, degilse dosya adi. Ikisi de yoksa URL'den tahmin.</summary>
    public string DisplayName =>
        !string.IsNullOrEmpty(TorrentName) ? TorrentName!
        : !string.IsNullOrEmpty(FilePath) ? Path.GetFileName(FilePath!)
        : !string.IsNullOrEmpty(SourceUri) ? UrlHelper.GuessFileName(SourceUri!)
        : Gid;

    public double Progress => TotalLength > 0 ? (double)CompletedLength / TotalLength : 0;

    public TimeSpan? Eta =>
        DownloadSpeed > 0 && TotalLength > CompletedLength
            ? TimeSpan.FromSeconds((TotalLength - CompletedLength) / (double)DownloadSpeed)
            : null;
}

public sealed class Aria2Exception(string message) : Exception(message);

/// <summary>
/// aria2 JSON-RPC istemcisi. Baglanti her zaman 127.0.0.1 uzerinden ve her istekte
/// "token:SECRET" ilk parametre olarak gider. Parolasiz istek aria2 tarafindan reddedilir.
/// </summary>
public sealed class Aria2Client : IDisposable
{
    private static readonly string[] StatusKeys =
    [
        "gid", "status", "totalLength", "completedLength", "downloadSpeed", "uploadSpeed",
        "errorCode", "errorMessage", "dir", "files", "infoHash", "numSeeders", "bittorrent"
    ];

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly string _endpoint;
    private readonly string _token;
    private int _id;

    public Aria2Client(Config config)
    {
        _endpoint = $"http://127.0.0.1:{config.RpcPort}/jsonrpc";
        _token = "token:" + config.RpcSecret;
    }

    // ---------------- RPC cekirdegi ----------------

    private async Task<string> PostAsync(string payload, CancellationToken ct)
    {
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(_endpoint, content, ct).ConfigureAwait(false);
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private async Task<JsonElement> CallAsync(string method, IEnumerable<object?> extraParams,
                                              CancellationToken ct = default)
    {
        var pars = new List<object?> { _token };
        pars.AddRange(extraParams);

        var payload = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = Interlocked.Increment(ref _id).ToString(),
            method,
            @params = pars
        });

        var body = await PostAsync(payload, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("error", out var err))
        {
            var m = err.TryGetProperty("message", out var mm) ? mm.GetString() : "bilinmeyen RPC hatasi";
            throw new Aria2Exception(method + ": " + m);
        }
        return doc.RootElement.GetProperty("result").Clone();
    }

    private Task<JsonElement> CallAsync(string method, CancellationToken ct = default)
        => CallAsync(method, [], ct);

    // ---------------- Saglik ----------------

    /// <summary>Motor ayakta degilse null doner, istisna firlatmaz.</summary>
    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await CallAsync("aria2.getVersion", ct).ConfigureAwait(false);
            return r.GetProperty("version").GetString();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    // ---------------- Ekleme ----------------

    /// <summary>HTTP/FTP/magnet URI ekler, GID doner.</summary>
    public async Task<string> AddUriAsync(string uri, Dictionary<string, object> options,
                                          CancellationToken ct = default)
    {
        var r = await CallAsync("aria2.addUri", [new[] { uri }, options], ct).ConfigureAwait(false);
        return r.GetString() ?? throw new Aria2Exception("addUri GID dondurmedi");
    }

    /// <summary>.torrent dosyasini base64 olarak ekler, GID doner.</summary>
    public async Task<string> AddTorrentAsync(byte[] torrent, Dictionary<string, object> options,
                                              CancellationToken ct = default)
    {
        var r = await CallAsync("aria2.addTorrent",
            [Convert.ToBase64String(torrent), Array.Empty<string>(), options], ct).ConfigureAwait(false);
        return r.GetString() ?? throw new Aria2Exception("addTorrent GID dondurmedi");
    }

    // ---------------- Sorgulama ----------------

    /// <summary>Aktif + bekleyen + biten hepsi tek istekte (system.multicall).</summary>
    public async Task<List<DownloadItem>> GetAllAsync(int stoppedLimit = 300, CancellationToken ct = default)
    {
        // system.multicall kendisi token almaz; token her alt cagrinin params'inda gider.
        var calls = new object[]
        {
            new { methodName = "aria2.tellActive",  @params = new object[] { _token, StatusKeys } },
            new { methodName = "aria2.tellWaiting", @params = new object[] { _token, 0, 1000, StatusKeys } },
            new { methodName = "aria2.tellStopped", @params = new object[] { _token, 0, stoppedLimit, StatusKeys } },
        };

        var payload = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = Interlocked.Increment(ref _id).ToString(),
            method = "system.multicall",
            @params = new object[] { calls }
        });

        var body = await PostAsync(payload, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("error", out var err))
            throw new Aria2Exception("system.multicall: " + err);

        var items = new List<DownloadItem>();
        foreach (var group in doc.RootElement.GetProperty("result").EnumerateArray())
        {
            // Her alt sonuc basarili ise [ [...] ], hatali ise { faultCode, faultString }.
            if (group.ValueKind != JsonValueKind.Array) continue;
            foreach (var arr in group.EnumerateArray())
            {
                if (arr.ValueKind != JsonValueKind.Array) continue;
                foreach (var el in arr.EnumerateArray())
                    items.Add(Parse(el));
            }
        }
        return items;
    }

    public async Task<DownloadItem?> TellStatusAsync(string gid, CancellationToken ct = default)
    {
        try
        {
            var r = await CallAsync("aria2.tellStatus", [gid, StatusKeys], ct).ConfigureAwait(false);
            return Parse(r);
        }
        catch (Aria2Exception) { return null; }
    }

    /// <summary>Torrent icindeki dosya listesi (secmeli indirme icin).</summary>
    public async Task<List<TorrentFile>> GetFilesAsync(string gid, CancellationToken ct = default)
    {
        var r = await CallAsync("aria2.getFiles", [gid], ct).ConfigureAwait(false);
        var list = new List<TorrentFile>();
        foreach (var f in r.EnumerateArray())
        {
            list.Add(new TorrentFile
            {
                Index = int.TryParse(Str(f, "index"), out var i) ? i : 0,
                Path = Str(f, "path") ?? "",
                Length = long.TryParse(Str(f, "length"), out var l) ? l : 0,
                Selected = Str(f, "selected") == "true",
            });
        }
        return list;
    }

    /// <summary>
    /// Magnet eklendiginde aria2 once metadata'yi indirir; asil torrent YENI bir GID alir
    /// ve eskisinin "followedBy" alaninda gorunur. Dosya secimi icin o GID gerekir.
    /// </summary>
    public async Task<string[]> GetFollowedByAsync(string gid, CancellationToken ct = default)
    {
        try
        {
            var r = await CallAsync("aria2.tellStatus", [gid, new[] { "followedBy" }], ct).ConfigureAwait(false);
            if (!r.TryGetProperty("followedBy", out var fb) || fb.ValueKind != JsonValueKind.Array)
                return [];
            return fb.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToArray();
        }
        catch (Aria2Exception) { return []; }
    }

    public async Task<GlobalStat> GetGlobalStatAsync(CancellationToken ct = default)
    {
        var r = await CallAsync("aria2.getGlobalStat", ct).ConfigureAwait(false);
        return new GlobalStat
        {
            DownloadSpeed = Num(r, "downloadSpeed"),
            UploadSpeed = Num(r, "uploadSpeed"),
            NumActive = (int)Num(r, "numActive"),
            NumWaiting = (int)Num(r, "numWaiting"),
            NumStopped = (int)Num(r, "numStoppedTotal"),
        };
    }

    // ---------------- Kontrol ----------------

    public Task PauseAsync(string gid, CancellationToken ct = default)
        => CallAsync("aria2.forcePause", [gid], ct);

    public Task UnpauseAsync(string gid, CancellationToken ct = default)
        => CallAsync("aria2.unpause", [gid], ct);

    /// <summary>Indirmeyi durdurup kuyruktan cikarir. Yarim dosya diskte kalir.</summary>
    public Task RemoveAsync(string gid, CancellationToken ct = default)
        => CallAsync("aria2.forceRemove", [gid], ct);

    /// <summary>Biten/hatali kaydi aria2 listesinden siler. Diskteki dosyaya dokunmaz.</summary>
    public Task RemoveResultAsync(string gid, CancellationToken ct = default)
        => CallAsync("aria2.removeDownloadResult", [gid], ct);

    public Task ChangeGlobalOptionAsync(Dictionary<string, string> options, CancellationToken ct = default)
        => CallAsync("aria2.changeGlobalOption", [options], ct);

    public Task ChangeOptionAsync(string gid, Dictionary<string, string> options, CancellationToken ct = default)
        => CallAsync("aria2.changeOption", [gid, options], ct);

    public Task SaveSessionAsync(CancellationToken ct = default)
        => CallAsync("aria2.saveSession", ct);

    /// <summary>Motoru duzgunce kapatir (kuyrugu kaydeder).</summary>
    public Task ShutdownAsync(CancellationToken ct = default)
        => CallAsync("aria2.shutdown", ct);

    // ---------------- Ayristirma ----------------

    private static string? Str(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long Num(JsonElement el, string name)
        => long.TryParse(Str(el, name), out var v) ? v : 0;

    private static DownloadItem Parse(JsonElement el)
    {
        string? filePath = null, sourceUri = null;

        if (el.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in files.EnumerateArray())
            {
                filePath ??= Str(f, "path");
                if (sourceUri is null && f.TryGetProperty("uris", out var uris)
                    && uris.ValueKind == JsonValueKind.Array)
                {
                    foreach (var u in uris.EnumerateArray())
                    {
                        sourceUri = Str(u, "uri");
                        if (sourceUri is not null) break;
                    }
                }
                if (filePath is not null && sourceUri is not null) break;
            }
        }

        string? torrentName = null;
        if (el.TryGetProperty("bittorrent", out var bt) && bt.ValueKind == JsonValueKind.Object
            && bt.TryGetProperty("info", out var info))
            torrentName = Str(info, "name");

        return new DownloadItem
        {
            Gid = Str(el, "gid") ?? "",
            Status = Str(el, "status") switch
            {
                "active" => Aria2Status.Active,
                "waiting" => Aria2Status.Waiting,
                "paused" => Aria2Status.Paused,
                "error" => Aria2Status.Error,
                "complete" => Aria2Status.Complete,
                "removed" => Aria2Status.Removed,
                _ => Aria2Status.Unknown,
            },
            TotalLength = Num(el, "totalLength"),
            CompletedLength = Num(el, "completedLength"),
            DownloadSpeed = Num(el, "downloadSpeed"),
            UploadSpeed = Num(el, "uploadSpeed"),
            ErrorCode = (int)Num(el, "errorCode"),
            ErrorMessage = Str(el, "errorMessage"),
            Dir = Str(el, "dir") ?? "",
            FilePath = string.IsNullOrEmpty(filePath) ? null : filePath,
            SourceUri = sourceUri,
            InfoHash = Str(el, "infoHash"),
            TorrentName = torrentName,
            NumSeeders = (int)Num(el, "numSeeders"),
        };
    }

    public void Dispose() => _http.Dispose();
}

public sealed class TorrentFile
{
    public int Index { get; init; }
    public string Path { get; init; } = "";
    public long Length { get; init; }
    public bool Selected { get; set; }
}

public sealed class GlobalStat
{
    public long DownloadSpeed { get; init; }
    public long UploadSpeed { get; init; }
    public int NumActive { get; init; }
    public int NumWaiting { get; init; }
    public int NumStopped { get; init; }
}
