using System.Windows;

namespace Akinti.Ui;

/// <summary>
/// Arsiv cikarma + sifre sorma akisi. Tek yerde duruyor, cunku ayni is iki
/// yoldan cagriliyor: sonuc penceresindeki "Klasore cikar" dugmesi ve indirme
/// bitiminde aria2 kancasinin calistirdigi otomatik cikarma.
///
/// Arsivciye once sifresiz denenir; "sifreli" cevabi gelirse kullaniciya sorulup
/// ayni cagri tekrarlanir. Sifre hicbir yere yazilmaz.
/// </summary>
public static class ExtractFlow
{
    private const int MaxAttempts = 5;

    public static async Task<ExtractResult> RunAsync(string archivePath, Config cfg, Window? owner = null)
    {
        string? password = null;
        var wrong = false;

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var result = await Archivers.ExtractAsync(archivePath, cfg, password).ConfigureAwait(true);
            if (result.Success || !result.NeedsPassword) return result;

            var entered = Ask(Path.GetFileName(archivePath), wrong, owner);
            if (entered is null)
                return new ExtractResult
                {
                    Success = false,
                    NeedsPassword = true,
                    Message = "Arşiv şifreli, çıkarılmadı."
                };

            password = entered;
            wrong = true;
        }

        return new ExtractResult { Success = false, Message = "Şifre birkaç kez yanlış girildi." };
    }

    /// <summary>Sifre penceresi her zaman arayuz is parcaciginda acilir: otomatik
    /// cikarma bir arka plan gorevinden geliyor.</summary>
    private static string? Ask(string archiveName, bool wrong, Window? owner)
    {
        var app = Application.Current;
        if (app is null) return null;

        return app.Dispatcher.Invoke(() =>
        {
            var dlg = new PasswordWindow(archiveName, wrong);
            if (owner is not null && owner.IsLoaded) dlg.Owner = owner;
            return dlg.ShowDialog() == true ? dlg.Password : null;
        });
    }
}
