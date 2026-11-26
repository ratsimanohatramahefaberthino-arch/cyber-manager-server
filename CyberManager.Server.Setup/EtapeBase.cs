using System.Text.Json;
using System.Text.Json.Serialization;
using CyberManager.Server.Common;

namespace CyberManager.Server.Setup;

public sealed class BilanInstaller
{
    public bool Ok { get; set; }
    public string? Base { get; set; }
    public string? Admin { get; set; }
    public int? PortHttp { get; set; }
    public string? CodeInstallation { get; set; }
    public List<string> Erreurs { get; set; } = new();
    public List<string> Avertissements { get; set; } = new();
}

/// <summary>
/// Lance « php artisan serveur:installer » avec le MÊME environnement que le service (EnvironnementPhp).
/// La demande (dont le mot de passe) passe par l'entrée standard : rien dans la ligne de commande, rien sur le disque,
/// et le JSON en UTF-8 évite les accents corrompus par les arguments Windows.
/// </summary>
internal static class EtapeBase
{
    public static EtapeResultat Executer(Demande d, RapportMiseEnPlace rapport)
    {
        if (!File.Exists(Chemins.PhpCli)) return EtapeResultat.Echouee($"php.exe est introuvable : {Chemins.PhpCli}");
        if (!File.Exists(Chemins.Artisan)) return EtapeResultat.Echouee($"artisan est introuvable : {Chemins.Artisan}");

        var charge = new Dictionary<string, object?>
        {
            ["nom_cyber"] = d.NomCyber,
            ["admin_nom"] = d.AdminNom,
            ["admin_email"] = d.AdminEmail,
            ["mot_de_passe"] = d.MotDePasse,
            ["port_http"] = d.PortHttp
        };

        var r = Execution.Lancer(
            Chemins.PhpCli,
            new[] { "artisan", "serveur:installer", "--stdin", "--json" },
            TimeSpan.FromMinutes(10),
            Chemins.App,
            JsonSerializer.Serialize(charge),
            environnementPhp: true);

        var bilan = JsonDernierObjet.Lire<BilanInstaller>(r.Sortie);
        if (bilan == null)
        {
            return EtapeResultat.Echouee("serveur:installer n'a renvoyé aucun résultat : " + r.Extrait());
        }

        rapport.Base = bilan.Base;
        rapport.Admin = bilan.Admin;
        rapport.CodeInstallation = bilan.CodeInstallation;
        if (bilan.PortHttp != null) rapport.PortHttp = bilan.PortHttp;

        if (!bilan.Ok)
        {
            return EtapeResultat.Echouee(bilan.Erreurs.Count > 0 ? string.Join(" ", bilan.Erreurs) : "serveur:installer a échoué : " + r.Extrait());
        }

        // Base créée ou complétée avec le code de cette version : le schéma est à jour, on le note.
        // (Sur une base existante, c'est le service qui sauvegarde, migre, puis écrit ce fichier.)
        if (bilan.Base is "creee" or "completee")
        {
            try
            {
                Directory.CreateDirectory(Chemins.Etat);
                File.WriteAllText(Chemins.VersionInstallee, rapport.VersionCible);
            }
            catch (Exception ex)
            {
                JournalInstall.Ecrire("Version installée non enregistrée : " + ex.Message);
            }
        }

        var detail = bilan.Base switch
        {
            "creee" => "Base de données créée et migrée, administrateur créé.",
            "completee" => "Base incomplète complétée (sauvegarde préalable), administrateur créé.",
            "existante" => "Base existante conservée telle quelle (le service la sauvegardera avant toute migration).",
            _ => "Base laissée inchangée."
        };

        var etape = bilan.Base == "existante" ? EtapeResultat.Gardee(detail) : EtapeResultat.Reussie(detail);
        etape.Alertes.AddRange(bilan.Avertissements);
        return etape;
    }
}