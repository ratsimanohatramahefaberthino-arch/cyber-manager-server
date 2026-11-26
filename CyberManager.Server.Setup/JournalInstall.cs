using System.Text;
using CyberManager.Server.Common;

namespace CyberManager.Server.Setup;

/// <summary>Journal de l'installation : ProgramData\CyberManager\logs\installation-AAAAMMJJ-HHmmss.log. Jamais de secret.</summary>
internal static class JournalInstall
{
    private static readonly object Verrou = new();
    private static string? _fichier;

    public static string? Fichier => _fichier;

    public static void Ecrire(string message)
    {
        var ligne = $"[{DateTime.Now:HH:mm:ss}] {message}";

        lock (Verrou)
        {
            try
            {
                Directory.CreateDirectory(Chemins.Logs);
                _fichier ??= Path.Combine(Chemins.Logs, $"installation-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                File.AppendAllText(_fichier, ligne + "\r\n", new UTF8Encoding(false));
            }
            catch
            {
                // le journal ne doit jamais faire échouer l'installation
            }
        }
    }
}