using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Akinti;

/// <summary>
/// Chrome native messaging host.
///
/// GUVENLIK SINIRI: Eklentiden gelen mesajdan SADECE "url" okunur.
/// Hedef klasor, arsiv cikarma ve "yonetici olarak calistir" gibi alanlar
/// bilerek yok sayilir - bunlar yalnizca kullanicinin gordugu diyalogda secilir.
/// Eklenti ele gecirilse bile bu yolla yonetici yetkisiyle bir sey calistirilamaz.
/// </summary>
public static class NativeHost
{
    private const int MaxMessageBytes = 1024 * 1024; // Chrome'un kendi siniri da 1 MB

    public static void Run(string[] args)
    {
        // args[0] = chrome-extension://<id>/ , args[1] = --parent-window=... (Chrome ekler)
        var origin = args.FirstOrDefault(a => a.StartsWith("chrome-extension://", StringComparison.Ordinal));
        Log.Info("Native host calisti. Kaynak: " + (origin ?? "belirtilmemis"));

        try
        {
            using var stdin = Console.OpenStandardInput();
            using var stdout = Console.OpenStandardOutput();

            var message = ReadMessage(stdin);
            if (message is null) { Log.Warn("Native host: mesaj okunamadi."); return; }

            var (ok, reason) = Handle(message);
            WriteMessage(stdout, JsonSerializer.Serialize(new { ok, reason }));
            stdout.Flush();
        }
        catch (Exception ex)
        {
            Log.Error("Native host hatasi", ex);
        }
    }

    private static (bool Ok, string Reason) Handle(string json)
    {
        string? url;
        try
        {
            using var doc = JsonDocument.Parse(json);
            // Tek okunan alan bu. Digerleri bilerek yok sayiliyor.
            url = doc.RootElement.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String
                ? u.GetString()
                : null;
        }
        catch (JsonException)
        {
            return (false, "gecersiz mesaj");
        }

        if (string.IsNullOrWhiteSpace(url))
            return (false, "url yok");

        if (!UrlHelper.IsSupportedScheme(url))
        {
            Log.Warn("Native host desteklenmeyen semayi reddetti: " + Truncate(url, 120));
            return (false, "desteklenmeyen adres");
        }

        try
        {
            // Diyalogu ayri bir surecte ac ve cik. Kalici bir kopru birakmiyoruz.
            //
            // UseShellExecute = true SART: false olsaydi diyalog, bu surecin Chrome'a
            // bagli stdin/stdout taniticilarini miras alirdi ve host cikse bile boru
            // acik kalirdi. ShellExecuteEx tanitici mirasi yapmaz.
            var psi = new ProcessStartInfo
            {
                FileName = AppPaths.ExePath,
                UseShellExecute = true,
            };
            psi.ArgumentList.Add("--new");
            psi.ArgumentList.Add(url!);
            Process.Start(psi);
            return (true, "diyalog acildi");
        }
        catch (Exception ex)
        {
            Log.Error("Diyalog acilamadi", ex);
            return (false, "diyalog acilamadi");
        }
    }

    // ---------------- Chrome native messaging cerceve formati ----------------

    private static string? ReadMessage(Stream stdin)
    {
        var header = new byte[4];
        if (!ReadExactly(stdin, header, 4)) return null;

        var length = BitConverter.ToInt32(header, 0);
        if (length <= 0 || length > MaxMessageBytes)
        {
            Log.Warn($"Native host gecersiz mesaj uzunlugu reddetti: {length}");
            return null;
        }

        var buffer = new byte[length];
        if (!ReadExactly(stdin, buffer, length)) return null;
        return Encoding.UTF8.GetString(buffer);
    }

    private static bool ReadExactly(Stream s, byte[] buffer, int count)
    {
        var read = 0;
        while (read < count)
        {
            var n = s.Read(buffer, read, count - read);
            if (n <= 0) return false;
            read += n;
        }
        return true;
    }

    private static void WriteMessage(Stream stdout, string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        stdout.Write(BitConverter.GetBytes(payload.Length), 0, 4);
        stdout.Write(payload, 0, payload.Length);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";
}
