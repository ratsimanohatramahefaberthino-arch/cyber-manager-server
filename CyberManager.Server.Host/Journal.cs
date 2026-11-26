using System.Text;
using CyberManager.Server.Common;

namespace CyberManager.Server.Host;

/// <summary>Journal du service : logs\service-AAAAMMJJ.log, rotation quotidienne, 30 jours conservés. Ne lève jamais d'exception.</summary>
internal static class Journal
{
    private const int JoursConserves = 30;
    private static readonly object Verrou = new();
    private static readonly UTF8Encoding Utf8 = new(false);
    private static string _jourPurge = "";

    /// <summary>Affiche aussi dans la console (mode test interactif).</summary>
    public static bool AfficherConsole { get; set; }

    public static void Ecrire(string message)
    {
        var maintenant = DateTime.Now;
        var ligne = $"{maintenant:yyyy-MM-dd HH:mm:ss.fff} {message}";

        lock (Verrou)
        {
            try
            {
                Directory.CreateDirectory(Chemins.Logs);
                var jour = maintenant.ToString("yyyyMMdd");
                File.AppendAllText(Path.Combine(Chemins.Logs, $"service-{jour}.log"), ligne + Environment.NewLine, Utf8);

                if (jour != _jourPurge)
                {
                    _jourPurge = jour;
                    Purger(maintenant);
                }
            }
            catch
            {
                // journal indisponible : on ne bloque jamais le service pour ça
            }

            if (AfficherConsole)
            {
                try { Console.WriteLine(ligne); } catch { }
            }
        }
    }

    private static void Purger(DateTime maintenant)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Chemins.Logs, "service-*.log"))
            {
                if (maintenant - File.GetLastWriteTime(f) > TimeSpan.FromDays(JoursConserves))
                {
                    File.Delete(f);
                }
            }
        }
        catch
        {
            // purge non critique
        }
    }
}