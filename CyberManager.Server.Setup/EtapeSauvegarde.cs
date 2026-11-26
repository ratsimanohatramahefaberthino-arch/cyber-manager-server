using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using CyberManager.Server.Common;

namespace CyberManager.Server.Setup;

/// <summary>
/// Dossier de sauvegarde choisi par le gérant (autre disque, clé USB) :
///  - validation stricte du chemin (lecteur local avec lettre, jamais le réseau : le compte virtuel du service
///    s'authentifie en tant que machine et n'a pas accès aux partages) ;
///  - création et test d'écriture ;
///  - droit « Modifier » accordé au compte du service ;
///  - choix enregistré dans config\sauvegarde.json  { "version": 1, "dossier": "E:\\Sauvegardes" }.
/// SauvegarderBase.php devra lire ce fichier (contrat à brancher à la sous-étape 4.4).
/// </summary>
internal static class EtapeSauvegarde
{
    public static EtapeResultat Executer(Demande d)
    {
        if (string.IsNullOrWhiteSpace(d.DossierSauvegarde))
        {
            return EtapeResultat.Ignoree("Aucun dossier choisi : dossier par défaut conservé (" + Chemins.Sauvegardes + ").");
        }

        var erreur = ValiderChemin(d.DossierSauvegarde);
        if (erreur != null) return EtapeResultat.Echouee(erreur);

        var dossier = Path.GetFullPath(d.DossierSauvegarde.Trim().Replace('/', '\\')).TrimEnd('\\');

        try
        {
            Directory.CreateDirectory(dossier);

            var test = Path.Combine(dossier, ".cm-test-" + Guid.NewGuid().ToString("N")[..8]);
            File.WriteAllText(test, "ok");
            File.Delete(test);
        }
        catch (Exception ex)
        {
            return EtapeResultat.Echouee($"Le dossier « {dossier} » n'est pas utilisable (disque absent, protégé ou plein ?) : {ex.Message}");
        }

        var etape = EtapeResultat.Reussie($"Dossier de sauvegarde prêt : {dossier}.");

        try
        {
            var service = SidServiceVirtuel.Pour(Constantes.NomService);
            var info = new DirectoryInfo(dossier);
            var securite = info.GetAccessControl();
            securite.SetAccessRule(new FileSystemAccessRule(
                service, FileSystemRights.Modify,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            info.SetAccessControl(securite);
        }
        catch (Exception ex) when (ex is NotSupportedException or UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            etape.Alertes.Add("Ce support ne gère pas les droits Windows (clé USB en FAT32 ?) : le service risque de ne pas pouvoir y écrire. "
                              + "Formatez-le en NTFS ou choisissez un disque interne. (" + ex.Message + ")");
        }

        EcrireChoix(dossier);
        return etape;
    }

    /// <summary>Renvoie un message d'erreur lisible, ou null si le chemin est acceptable.</summary>
    internal static string? ValiderChemin(string? chemin)
    {
        if (string.IsNullOrWhiteSpace(chemin)) return "Aucun dossier de sauvegarde n'a été indiqué.";

        var c = chemin.Trim().Replace('/', '\\');

        if (c.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return "Un dossier réseau ne convient pas : le service Windows n'y a pas accès. Choisissez un disque ou une clé USB de cet ordinateur.";
        }

        if (c.Length < 3 || !char.IsAsciiLetter(c[0]) || c[1] != ':' || c[2] != '\\')
        {
            return "Indiquez un dossier complet, avec la lettre du disque (par exemple E:\\Sauvegardes).";
        }

        if (c.TrimEnd('\\').Length <= 2) return "Choisissez un dossier précis, pas la racine du disque.";

        if (c.Split('\\').Any(s => s == "..")) return "Le dossier ne doit pas contenir « .. ».";

        string complet;
        try
        {
            complet = Path.GetFullPath(c);
        }
        catch (Exception)
        {
            return "Le dossier indiqué n'est pas valide.";
        }

        foreach (var interdit in new[] { Chemins.Code, Environment.GetFolderPath(Environment.SpecialFolder.Windows) })
        {
            if (string.IsNullOrEmpty(interdit)) continue;

            var racine = interdit.TrimEnd('\\') + "\\";
            if ((complet.TrimEnd('\\') + "\\").StartsWith(racine, StringComparison.OrdinalIgnoreCase))
            {
                return "Ce dossier est réservé au programme ou à Windows : choisissez-en un autre.";
            }
        }

        return null;
    }

    private static void EcrireChoix(string dossier)
    {
        Directory.CreateDirectory(Chemins.Config);

        var fichier = Path.Combine(Chemins.Config, "sauvegarde.json");
        var temporaire = fichier + ".tmp";

        File.WriteAllText(
            temporaire,
            JsonSerializer.Serialize(new Dictionary<string, object> { ["version"] = 1, ["dossier"] = dossier },
                new JsonSerializerOptions { WriteIndented = true }) + "\n");

        File.Move(temporaire, fichier, true);
    }
}