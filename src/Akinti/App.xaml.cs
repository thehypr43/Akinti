using System.Windows;
using Akinti.Ui;

namespace Akinti;

/// <summary>
/// Tek exe, cok mod. Hangi modda calisildigi argumanlardan anlasilir:
///
///   (bos)                         -> Ana pencere
///   --new &lt;url&gt;             -> Yeni indirme diyalogu
///   chrome-extension://...        -> Chrome native messaging host (Chrome boyle baslatir)
///   &lt;gid&gt; &lt;n&gt; &lt;yol&gt; -> aria2 indirme-sonrasi kancasi (aria2 boyle cagirir)
///   --on-complete &lt;gid&gt; ... -> ayni kanca, elle test icin
///
/// Uygulama WinExe (GUI subsystem) oldugu icin kanca ve native host modlarinda
/// hicbir konsol penceresi acilmaz.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            Dispatch(e.Args);
        }
        catch (Exception ex)
        {
            Log.Error("Baslatma hatasi", ex);
            Shutdown(1);
        }
    }

    private void Dispatch(string[] args)
    {
        // --- Chrome native messaging host ---
        if (args.Any(a => a.StartsWith("chrome-extension://", StringComparison.Ordinal))
            || args.Contains("--native-host"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            NativeHost.Run(args);
            Shutdown();
            return;
        }

        // --- aria2 indirme-sonrasi kancasi ---
        // aria2 komuta tam olarak 3 arguman gecer: GID, dosya sayisi, dosya yolu.
        if (args.Length == 3 && PostActions.LooksLikeGid(args[0]))
        {
            RunHookThenExit(args[0], args[2]);
            return;
        }
        if (args.Length >= 2 && args[0] == "--on-complete")
        {
            RunHookThenExit(args[1], args.Length >= 4 ? args[3] : null);
            return;
        }

        // --- Kurulum scriptinin cagirdigi: config.json'dan aria2.conf uret ---
        if (args.Contains("--write-conf"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Aria2Engine.WriteConfFile(Config.Load());
            Log.Info("aria2.conf yeniden uretildi.");
            Shutdown();
            return;
        }

        // --- Yeni indirme diyalogu ---
        var newIndex = Array.IndexOf(args, "--new");
        if (newIndex >= 0 && newIndex + 1 < args.Length)
        {
            ShutdownMode = ShutdownMode.OnLastWindowClose;

            var refIndex = Array.IndexOf(args, "--referrer");
            var referrer = refIndex >= 0 && refIndex + 1 < args.Length ? args[refIndex + 1] : null;

            var win = new NewDownloadWindow(args[newIndex + 1], referrer);
            win.Show();
            return;
        }

        // --- Ana pencere ---
        ShutdownMode = ShutdownMode.OnLastWindowClose;
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private void RunHookThenExit(string gid, string? reportedPath)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _ = Task.Run(async () =>
        {
            try { await PostActions.HandleAsync(gid, reportedPath).ConfigureAwait(false); }
            catch (Exception ex) { Log.Error("Kanca hatasi", ex); }
            finally { Dispatcher.Invoke(() => Shutdown()); }
        });
    }
}
