namespace CyberManager.Server.Control;

internal static class Program
{
    internal const string NomMutex = @"Local\CyberManagerServerControl";
    internal const string NomEvenement = @"Local\CyberManagerServerControl.Afficher";

    [STAThread]
    private static int Main(string[] args)
    {
        var minimise = args.Any(a => a.Equals("--minimise", StringComparison.OrdinalIgnoreCase));

        using var mutex = new Mutex(true, NomMutex, out var premiere);
        if (!premiere)
        {
            // Une télécommande tourne déjà : on la réveille (sauf lancement silencieux au démarrage de Windows)
            if (!minimise)
            {
                try
                {
                    using var evenement = EventWaitHandle.OpenExisting(NomEvenement);
                    evenement.Set();
                }
                catch
                {
                    // l'autre instance démarre à peine : sans importance
                }
            }

            return 0;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.ThreadException += (_, e) => JournalControle.Ecrire("Exception UI non gérée : " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => JournalControle.Ecrire("Exception non gérée : " + e.ExceptionObject);

        try
        {
            Application.Run(new ControlApplicationContext(minimise));
        }
        catch (Exception ex)
        {
            JournalControle.Ecrire("Erreur fatale : " + ex);
            return 1;
        }

        return 0;
    }
}