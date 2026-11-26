using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CyberManager.Server.Common;

namespace CyberManager.Server.Control;

/// <summary>
/// Zip de diagnostic : logs, état, versions. Ne contient jamais .env, serveur.json ni la base ;
/// les valeurs ressemblant à des secrets (APP_KEY, mots de passe, tokens…) sont masquées.
/// </summary>
internal static class DiagnosticExporter
{
    private const long TailleMaxParFichier = 2L * 1024 * 1024;
    private const int NombreMaxJournaux = 12;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly Regex Secrets = new(
        @"(?i)\b(app_key|password|passwd|mot_de_passe|secret|token|api[_-]?key|authorization|code_installation|bearer)([""']?\s*[=:]\s*)(""[^""]*""|'[^']*'|[^\s,;]+)",
        RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    private static readonly Regex CleBase64 = new(
        @"base64:[A-Za-z0-9+/=]{16,}", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    public static void Exporter(string cheminZip, InstantaneServeur instantane)
    {
        var temporaire = cheminZip + ".tmp";

        try
        {
            using (var flux = new FileStream(temporaire, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            using (var zip = new ZipArchive(flux, ZipArchiveMode.Create, leaveOpen: false))
            {
                AjouterTexte(zip, "infos.txt", Infos(instantane));

                if (instantane.Statut != null)
                {
                    AjouterTexte(zip, "statut.json", JsonSerializer.Serialize(instantane.Statut, JsonOptions));
                }

                AjouterFichier(zip, Chemins.PortsJson, "runtime/ports.json");
                AjouterFichier(zip, Chemins.Caddyfile, "runtime/Caddyfile");
                AjouterFichier(zip, JournalControle.Fichier, "logs-telecommande/controle.log");

                foreach (var (chemin, nom) in FichiersJournaux())
                {
                    AjouterFichier(zip, chemin, nom);
                }
            }

            File.Move(temporaire, cheminZip, true);
        }
        catch
        {
            try
            {
                if (File.Exists(temporaire)) File.Delete(temporaire);
            }
            catch
            {
                // sans importance
            }

            throw;
        }
    }

    // ------------------------------------------------------------------

    private static IEnumerable<(string Chemin, string Nom)> FichiersJournaux()
    {
        var trouves = new List<(FileInfo Fichier, string Prefixe)>();

        foreach (var (dossier, prefixe) in new[]
                 {
                     (Chemins.Logs, "logs"),
                     (Path.Combine(Chemins.Storage, "logs"), "logs-laravel")
                 })
        {
            try
            {
                if (!Directory.Exists(dossier)) continue;

                foreach (var f in new DirectoryInfo(dossier).GetFiles("*.log"))
                {
                    trouves.Add((f, prefixe));
                }
            }
            catch
            {
                // dossier illisible : on continue avec le reste
            }
        }

        return trouves
            .OrderByDescending(t => t.Fichier.LastWriteTimeUtc)
            .Take(NombreMaxJournaux)
            .Select(t => (t.Fichier.FullName, t.Prefixe + "/" + t.Fichier.Name))
            .ToList();
    }

    private static string Infos(InstantaneServeur s)
    {
        var sb = new StringBuilder();
        var maintenant = DateTimeOffset.Now.ToOffset(Presentation.DecalageMadagascar);

        sb.AppendLine("Cyber Manager — diagnostic de la télécommande");
        sb.AppendLine($"Date (Madagascar, UTC+3) : {maintenant:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Ordinateur               : {Environment.MachineName}");
        sb.AppendLine($"Système                  : {RuntimeInformation.OSDescription}");
        sb.AppendLine($".NET                     : {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"Télécommande             : {Assembly.GetExecutingAssembly().GetName().Version}");
        sb.AppendLine($"Service (version hôte)   : {Constantes.VersionHote}");
        sb.AppendLine();
        sb.AppendLine($"Liaison avec le service  : {s.Liaison}");
        if (!string.IsNullOrWhiteSpace(s.Detail)) sb.AppendLine($"Détail de la liaison     : {s.Detail}");
        if (s.Statut != null)
        {
            sb.AppendLine($"État du serveur          : {s.Statut.Etat}");
            sb.AppendLine($"Message                  : {s.Statut.Message}");
        }

        sb.AppendLine();
        sb.AppendLine($"Dossier de code          : {Chemins.Code}");
        sb.AppendLine($"Dossier de l'application : {Chemins.App}");
        sb.AppendLine($"Dossier de données       : {Chemins.Donnees}");
        sb.AppendLine($"caddy.exe présent        : {File.Exists(Chemins.Caddy)}");
        sb.AppendLine($"php.exe présent          : {File.Exists(Chemins.PhpCli)}");
        sb.AppendLine($"php-cgi.exe présent      : {File.Exists(Chemins.PhpCgi)}");
        sb.AppendLine($"Base SQLite présente     : {File.Exists(Chemins.BaseSqlite)}");

        try
        {
            sb.AppendLine($"arret_propre.flag        : {(File.Exists(Chemins.FlagArretPropre) ? File.ReadAllText(Chemins.FlagArretPropre).Trim() : "absent")}");
            sb.AppendLine($"Version installée        : {(File.Exists(Chemins.VersionInstallee) ? File.ReadAllText(Chemins.VersionInstallee).Trim() : "inconnue")}");
        }
        catch
        {
            sb.AppendLine("Fichiers d'état          : illisibles");
        }

        try
        {
            var racine = Path.GetPathRoot(Chemins.Donnees);
            if (!string.IsNullOrEmpty(racine))
            {
                sb.AppendLine($"Espace libre sur {racine}  : {new DriveInfo(racine).AvailableFreeSpace / (1024 * 1024)} Mo");
            }
        }
        catch
        {
            // information facultative
        }

        sb.AppendLine();
        sb.AppendLine("Les secrets (APP_KEY, mots de passe, tokens) ne sont jamais inclus dans cette archive.");
        return sb.ToString();
    }

    // ------------------------------------------------------------------

    private static void AjouterFichier(ZipArchive zip, string chemin, string nom)
    {
        try
        {
            if (!File.Exists(chemin)) return;
            AjouterTexte(zip, nom, Masquer(LireQueue(chemin)));
        }
        catch (Exception ex)
        {
            AjouterTexte(zip, nom + ".erreur.txt", "Fichier non lisible : " + ex.Message);
        }
    }

    private static void AjouterTexte(ZipArchive zip, string nom, string contenu)
    {
        var entree = zip.CreateEntry(nom, CompressionLevel.Optimal);
        using var ecrivain = new StreamWriter(entree.Open(), new UTF8Encoding(false));
        ecrivain.Write(contenu);
    }

    /// <summary>Lit la fin du fichier (les logs sont souvent verrouillés par leur propriétaire : partage maximal).</summary>
    private static string LireQueue(string chemin)
    {
        using var flux = new FileStream(chemin, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        var tronque = flux.Length > TailleMaxParFichier;
        if (tronque) flux.Seek(-TailleMaxParFichier, SeekOrigin.End);

        using var lecteur = new StreamReader(flux, Encoding.UTF8);
        var texte = lecteur.ReadToEnd();

        if (tronque)
        {
            var i = texte.IndexOf('\n');
            texte = "[… début du fichier tronqué …]\n" + (i >= 0 ? texte[(i + 1)..] : texte);
        }

        return texte;
    }

    private static string Masquer(string texte)
    {
        try
        {
            texte = Secrets.Replace(texte, "$1$2[MASQUE]");
            return CleBase64.Replace(texte, "base64:[MASQUE]");
        }
        catch (RegexMatchTimeoutException)
        {
            return "[contenu omis : le masquage des secrets n'a pas pu être garanti]";
        }
    }
}