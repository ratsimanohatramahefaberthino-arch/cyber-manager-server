namespace CyberManager.Server.Control;

/// <summary>Petit journal local de la télécommande : %LocalAppData%\CyberManager\controle.log (1 Mo max, une archive).</summary>
internal static class JournalControle
{
    private static readonly object Verrou = new();

    public static string Dossier =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CyberManager");

    public static string Fichier => Path.Combine(Dossier, "controle.log");

    public static void Ecrire(string message)
    {
        try
        {
            lock (Verrou)
            {
                Directory.CreateDirectory(Dossier);

                var info = new FileInfo(Fichier);
                if (info.Exists && info.Length > 1_000_000)
                {
                    File.Move(Fichier, Fichier + ".old", true);
                }

                File.AppendAllText(Fichier, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // le journal ne doit jamais faire planter la télécommande
        }
    }
}