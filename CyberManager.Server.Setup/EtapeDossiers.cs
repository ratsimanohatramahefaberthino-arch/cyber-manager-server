using System.Security.AccessControl;
using System.Security.Principal;
using CyberManager.Server.Common;

namespace CyberManager.Server.Setup;

/// <summary>
/// Crée l'arborescence ProgramData\CyberManager et fixe les droits NTFS (idempotent, rejouable) :
///  - racine : héritage coupé ; Système + Administrateurs = Contrôle total ; compte du service = Modifier ;
///    les utilisateurs ordinaires n'ont PAS accès (config\.env contient la clé de chiffrement, data\ les mots de passe hachés) ;
///  - la télécommande (lancée par le gérant) peut LIRE uniquement les journaux, runtime (ports.json) et etat ;
///  - le compte du service lit le code dans Program Files (jamais d'écriture : code en lecture seule).
/// Les SID sont utilisés à la place des noms : « Utilisateurs » ou « Users » selon la langue de Windows.
/// </summary>
internal static class EtapeDossiers
{
    public static EtapeResultat Executer()
    {
        Chemins.AssurerDossiers();

        var service = SidServiceVirtuel.Pour(Constantes.NomService);
        var systeme = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var utilisateurs = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var authentifies = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
        var tous = new SecurityIdentifier(WellKnownSidType.WorldSid, null);

        // 1. Racine des données
        var racine = new DirectoryInfo(Chemins.Donnees);
        var securite = racine.GetAccessControl();
        securite.SetAccessRuleProtection(true, true);   // coupe l'héritage de ProgramData, en copiant les règles héritées

        foreach (var sid in new[] { utilisateurs, authentifies, tous })
        {
            securite.PurgeAccessRules(sid);
        }

        securite.SetAccessRule(Regle(systeme, FileSystemRights.FullControl));
        securite.SetAccessRule(Regle(admins, FileSystemRights.FullControl));
        securite.SetAccessRule(Regle(service, FileSystemRights.Modify));
        racine.SetAccessControl(securite);

        // 2. Lecture seule pour la télécommande (diagnostic, adresses, état)
        var lisibles = new[]
        {
            Chemins.Logs,
            Chemins.Runtime,
            Chemins.Etat,
            Path.Combine(Chemins.Storage, "logs")
        };

        foreach (var dossier in lisibles)
        {
            var info = new DirectoryInfo(dossier);
            var s = info.GetAccessControl();
            s.SetAccessRule(Regle(utilisateurs, FileSystemRights.ReadAndExecute));
            info.SetAccessControl(s);
        }

        // 3. Le service doit pouvoir lire le code (Program Files), sans jamais y écrire
        var detail = "Droits appliqués : Système et Administrateurs (contrôle total), service (modifier), utilisateurs (lecture des journaux seulement).";

        if (Directory.Exists(Chemins.Code))
        {
            var code = new DirectoryInfo(Chemins.Code);
            var s = code.GetAccessControl();
            s.SetAccessRule(Regle(service, FileSystemRights.ReadAndExecute));
            code.SetAccessControl(s);
        }
        else
        {
            detail += " (dossier de code absent : droits de lecture du service non appliqués.)";
        }

        return EtapeResultat.Reussie(detail);
    }

    private static FileSystemAccessRule Regle(SecurityIdentifier sid, FileSystemRights droits) =>
        new(sid, droits, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow);
}