using System.Text;
using CyberManager.Server.Common;

namespace CyberManager.Server.Host;

/// <summary>Sortie console des processus enfants : logs\enfant-&lt;fichier&gt;.log, plafonné à 5 Mo (+ une copie .1.log).</summary>
internal static class JournalEnfant
{
    private const long TailleMax = 5L * 1024 * 1024;
    private static readonly object Verrou = new();
    private static readonly UTF8Encoding Utf8 = new(false);

    public static void Ecrire(string fichier, string nom, string? ligne)
    {
        if (string.IsNullOrEmpty(ligne)) return;

        lock (Verrou)
        {
            try
            {
                Directory.CreateDirectory(Chemins.Logs);

                var chemin = Path.Combine(Chemins.Logs, $"enfant-{fichier}.log");
                var info = new FileInfo(chemin);
                if (info.Exists && info.Length > TailleMax)
                {
                    File.Move(chemin, Path.Combine(Chemins.Logs, $"enfant-{fichier}.1.log"), true);
                }

                File.AppendAllText(chemin, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{nom}] {ligne}{Environment.NewLine}", Utf8);
            }
            catch
            {
                // non critique
            }
        }
    }
}