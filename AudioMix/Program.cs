namespace AudioMix;

internal static class Program
{
    private const string MutexName = @"Local\AudioMix.Instance";
    private const string ShowEventName = @"Local\AudioMix.ShowSettings";

    [STAThread]
    private static void Main(string[] args)
    {
        // Nur eine Instanz zulassen – zwei Balancer würden sich gegenseitig stören.
        using var mutex = new Mutex(true, MutexName, out bool isFirstInstance);

        // Signal, mit dem eine zweite Instanz die laufende bittet, das Fenster zu zeigen.
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        if (!isFirstInstance)
        {
            showEvent.Set();
            return;
        }

        ApplicationConfiguration.Initialize();

        // Windows startet uns per Autostart mit "--background": dann kein Fenster, nur Tray-Symbol.
        bool startHidden = args.Contains("--background", StringComparer.OrdinalIgnoreCase);

        using var context = new TrayApplicationContext(startHidden);

        // Wartet ohne eigenen Thread (Thread-Pool-Wait) auf das Signal einer zweiten Instanz.
        var registration = ThreadPool.RegisterWaitForSingleObject(
            showEvent, (_, _) => context.RequestShowSettings(), null, Timeout.Infinite, executeOnlyOnce: false);

        Application.Run(context);

        registration.Unregister(null);
    }
}