using Microsoft.Data.Sqlite;
using CyberManager.Server.Common;

namespace CyberManager.Server.Setup;

/// <summary>
/// Règles du Pare-feu Windows LIÉES AUX PROGRAMMES (pas à un numéro de port TCP : le port web peut changer, Règle 1) :
///  - caddy.exe, TCP entrant, tous ports, toutes adresses réseau (*) ;
///  - php.exe (cyber:decouverte), UDP entrant, sur le port de découverte configuré.
/// Tous profils (Privé, Public, Domaine), accessible depuis tous les sous-réseaux et cartes réseau.
/// Les règles sont créées automatiquement par l'installateur graphique (Règle 2 : zéro terminal pour le gérant).
/// </summary>
internal static class EtapeParefeu
{
    private const int ProtocoleTcp = 6;
    private const int ProtocoleUdp = 17;
    private const int Entrant = 1;
    private const int Autoriser = 1;
    private const int TousProfils = 0x7FFFFFFF;

    public static EtapeResultat Executer()
    {
        var caddy = Chemins.Caddy;
        var php = Chemins.PhpCli;

        if (!File.Exists(caddy)) return EtapeResultat.Echouee($"caddy.exe est introuvable : {caddy}");
        if (!File.Exists(php)) return EtapeResultat.Echouee($"php.exe est introuvable : {php}");

        var udp = LirePortUdp();

        var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2", false);
        if (type == null) return EtapeResultat.Echouee("Le Pare-feu Windows n'est pas disponible (composant HNetCfg.FwPolicy2 introuvable).");

        dynamic politique = Activator.CreateInstance(type)!;
        dynamic regles = politique.Rules;

        var etape = EtapeResultat.Reussie("");
        var anciennes = NettoyerAnciennesRegles(regles);

        // Règle Web Caddy : autorisée pour TOUTES les adresses distantes (*) pour fonctionner sur n'importe quel réseau (Règle 1 & 3)
        Remplacer(regles, Disposition.NomRegleWeb, caddy, ProtocoleTcp, null,
            "Autorise les postes clients et téléphones à joindre le serveur Cyber Manager.");

        // Règle Découverte UDP : autorisée pour retrouver le serveur dynamiquement
        Remplacer(regles, Disposition.NomRegleDecouverte, php, ProtocoleUdp, udp.ToString(),
            "Permet aux postes de retrouver seuls le serveur Cyber Manager sur le réseau local.");

        // Vérification : les deux règles existent et sont activées
        foreach (var nom in new[] { Disposition.NomRegleWeb, Disposition.NomRegleDecouverte })
        {
            try
            {
                dynamic regle = regles.Item(nom);
                if (!(bool)regle.Enabled) return EtapeResultat.Echouee($"La règle « {nom} » existe mais n'est pas activée.");
            }
            catch
            {
                return EtapeResultat.Echouee($"La règle « {nom} » n'a pas pu être créée dans le Pare-feu Windows.");
            }
        }

        etape.Detail = $"Règles créées : Caddy (TCP, tous réseaux) et découverte des postes (UDP {udp})."
                       + (anciennes > 0 ? $" {anciennes} ancienne(s) règle(s) retirée(s)." : "");
        return etape;
    }

    private static void Remplacer(dynamic regles, string nom, string programme, int protocole, string? portLocal, string description)
    {
        Supprimer(regles, nom);

        dynamic r = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule", true)!)!;
        r.Name = nom;
        r.Description = description;
        r.ApplicationName = programme;
        r.Protocol = protocole;
        if (portLocal != null) r.LocalPorts = portLocal;
        r.Direction = Entrant;
        r.Action = Autoriser;
        r.Profiles = TousProfils;
        r.RemoteAddresses = "*"; // TOUS les sous-réseaux autorisés : évite le blocage des cartes 4G, Wi-Fi et réseaux multiples
        r.Grouping = "Cyber Manager";
        r.Enabled = true;

        regles.Add(r);
    }

    private static void Supprimer(dynamic regles, string nom)
    {
        for (var i = 0; i < 10; i++)
        {
            try { regles.Remove(nom); }
            catch { break; }
        }
    }

    private static int NettoyerAnciennesRegles(dynamic regles)
    {
        var aSupprimer = new List<string>();

        try
        {
            foreach (dynamic r in regles)
            {
                string nom = r.Name;
                if (nom.StartsWith("Cyber Manager (port ", StringComparison.Ordinal)
                    || nom.StartsWith("Cyber Manager découverte (UDP", StringComparison.Ordinal))
                {
                    aSupprimer.Add(nom);
                }
            }
        }
        catch (Exception ex)
        {
            JournalInstall.Ecrire("Énumération des anciennes règles impossible : " + ex.Message);
        }

        foreach (var nom in aSupprimer.Distinct()) Supprimer(regles, nom);

        return aSupprimer.Count;
    }

    internal static int LirePortUdp()
    {
        try
        {
            if (!File.Exists(Chemins.BaseSqlite)) return Disposition.PortUdpDefaut;

            var chaine = new SqliteConnectionStringBuilder
            {
                DataSource = Chemins.BaseSqlite,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString();

            using var c = new SqliteConnection(chaine);
            c.Open();

            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT valeur FROM cyber_settings WHERE cle = 'resilience.port_decouverte_udp'";

            return int.TryParse(cmd.ExecuteScalar() as string, out var p) && p is >= 1024 and <= 65535
                ? p
                : Disposition.PortUdpDefaut;
        }
        catch
        {
            return Disposition.PortUdpDefaut;
        }
    }
}