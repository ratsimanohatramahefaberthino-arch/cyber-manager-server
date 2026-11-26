using System.Globalization;
using System.Net;
using System.Text;
using CyberManager.Server.Common;
using CyberManager.Server.Common.Ipc;

namespace CyberManager.Server.Control;

internal enum Niveau { Vert, Orange, Rouge, Gris }

internal sealed record VueGlobale(Niveau Niveau, string Titre, string Message);

internal readonly record struct Droits(
    bool Demarrer,
    bool Arreter,
    bool Redemarrer,
    bool Sauvegarder,
    bool Code,
    bool Ouvrir,
    bool CopierAdresse);

/// <summary>Règles d'affichage partagées par la fenêtre et le menu de la zone de notification.</summary>
internal static class Presentation
{
    /// <summary>Madagascar : UTC+3 toute l'année, sans heure d'été.</summary>
    public static readonly TimeSpan DecalageMadagascar = TimeSpan.FromHours(3);

    public static Color Couleur(Niveau n) => n switch
    {
        Niveau.Vert => Color.FromArgb(0x2E, 0x9E, 0x4F),
        Niveau.Orange => Color.FromArgb(0xF0, 0xA0, 0x20),
        Niveau.Rouge => Color.FromArgb(0xD9, 0x36, 0x36),
        _ => Color.FromArgb(0x80, 0x86, 0x90)
    };

    /// <summary>Version plus sombre, lisible comme couleur de texte.</summary>
    public static Color CouleurTexte(Niveau n) => n switch
    {
        Niveau.Vert => Color.FromArgb(0x1E, 0x7B, 0x3A),
        Niveau.Orange => Color.FromArgb(0xB3, 0x6B, 0x00),
        Niveau.Rouge => Color.FromArgb(0xB4, 0x23, 0x18),
        _ => Color.FromArgb(0x55, 0x5B, 0x66)
    };

    public static VueGlobale Resumer(InstantaneServeur s)
    {
        switch (s.Liaison)
        {
            case Liaison.Connexion:
                return new VueGlobale(Niveau.Gris, "Connexion au serveur…", "Recherche du service Cyber Manager sur cet ordinateur.");

            case Liaison.Reconnexion:
                return new VueGlobale(Niveau.Gris, "Reconnexion en cours…",
                    string.IsNullOrWhiteSpace(s.Detail)
                        ? "Le service ne répond pas pour le moment : nouvelle tentative automatique."
                        : s.Detail);

            case Liaison.ServiceAbsent:
                return new VueGlobale(Niveau.Rouge, "Service introuvable",
                    "Cyber Manager n'est pas installé correctement sur cet ordinateur. Cliquez sur « Réparer ».");

            case Liaison.ServiceArrete:
                return new VueGlobale(Niveau.Rouge, "Service arrêté",
                    "Le service Windows de Cyber Manager est arrêté : les postes ne peuvent plus se connecter. Cliquez sur « Réparer » pour le relancer.");

            case Liaison.Injoignable:
                return new VueGlobale(Niveau.Rouge, "Service injoignable",
                    "Le service est lancé mais ne répond pas. Cliquez sur « Réparer » pour le relancer ; si cela persiste, exportez le diagnostic.");
        }

        var st = s.Statut ?? new StatutServeur();
        var message = string.IsNullOrWhiteSpace(st.Message) ? "" : st.Message;

        switch (st.Etat)
        {
            case EtatServeur.Pret:
                return new VueGlobale(Niveau.Vert, "Serveur opérationnel",
                    st.SessionsEnCours is > 0 and var n
                        ? $"Les postes peuvent se connecter · {Sessions(n)} en cours."
                        : "Les postes peuvent se connecter.");

            case EtatServeur.Degrade:
                return new VueGlobale(Niveau.Orange, "Serveur dégradé",
                    message.Length > 0 ? message : "Le serveur fonctionne, mais un élément demande votre attention.");

            case EtatServeur.Demarrage:
                return new VueGlobale(Niveau.Orange, "Démarrage en cours…",
                    message.Length > 0 ? message : "Le serveur se met en route, merci de patienter.");

            case EtatServeur.Arrete:
                return new VueGlobale(Niveau.Rouge, "Serveur arrêté",
                    "Les postes ne peuvent plus se connecter. Cliquez sur « Démarrer » pour relancer le serveur.");

            case EtatServeur.EnPanne:
                return new VueGlobale(Niveau.Rouge, "Serveur en panne",
                    message.Length > 0 ? message : "Un élément essentiel ne démarre pas. Essayez « Redémarrer », sinon exportez le diagnostic.");

            case EtatServeur.BaseARestaurer:
                return new VueGlobale(Niveau.Rouge, "Base de données à restaurer",
                    message.Length > 0 ? message : "La base de données est endommagée : une restauration de la dernière sauvegarde est nécessaire.");

            case EtatServeur.HorlogeAVerifier:
                return new VueGlobale(Niveau.Orange, "Heure de l'ordinateur à vérifier",
                    message.Length > 0 ? message : "L'heure de Windows paraît incorrecte : aucune session n'est figée ni clôturée tant qu'elle n'est pas confirmée.");

            default:
                return new VueGlobale(Niveau.Orange, st.Etat, message);
        }
    }

    public static Droits CalculerDroits(InstantaneServeur s, bool occupe)
    {
        var st = s.Statut;
        var connecte = s.Liaison == Liaison.Connecte && st != null;
        var etat = st?.Etat ?? "";
        var adresse = MeilleureUrl(st) != null;

        return new Droits(
            Demarrer: connecte && !occupe && etat != EtatServeur.Pret && etat != EtatServeur.Demarrage,
            Arreter: connecte && !occupe && etat != EtatServeur.Arrete && etat != EtatServeur.Demarrage,
            Redemarrer: connecte && !occupe && etat != EtatServeur.Demarrage,
            Sauvegarder: connecte && !occupe,
            Code: connecte && !occupe,
            Ouvrir: adresse && st != null && etat != EtatServeur.Arrete,
            CopierAdresse: adresse);
    }

    public static string? MeilleureUrl(StatutServeur? st)
    {
        var urls = st?.Urls;
        if (urls == null || urls.Count == 0) return null;

        foreach (var u in urls)
        {
            if (!EstBoucleLocale(u)) return u;
        }

        return urls[0];
    }

    public static bool EstBoucleLocale(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        var hote = uri.Host;
        return hote.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               || (IPAddress.TryParse(hote, out var ip) && IPAddress.IsLoopback(ip));
    }

    public static bool EstIncident(IncidentCoupure? i)
    {
        if (i == null) return false;

        return i.Id != null
               || i.Type.Contains("coupure", StringComparison.OrdinalIgnoreCase)
               || i.Type.Contains("horloge", StringComparison.OrdinalIgnoreCase);
    }

    public static string CleIncident(IncidentCoupure i) =>
        $"{i.Type}|{i.Id}|{i.EcartSecondes}|{i.SessionsFigees}|{i.MontantFige}";

    public static bool EstHorloge(IncidentCoupure i) => i.Type.Contains("horloge", StringComparison.OrdinalIgnoreCase);

    public static string TexteIncident(StatutServeur st)
    {
        var i = st.Incident!;

        if (EstHorloge(i))
        {
            return "L'heure de cet ordinateur paraissait incorrecte au démarrage (pile du BIOS, synchronisation…). "
                   + "Par prudence, aucune session n'a été figée ni clôturée automatiquement. "
                   + "Vérifiez l'heure de Windows, puis confirmez-la dans l'interface web (page Incidents).";
        }

        var sb = new StringBuilder("Arrêt anormal détecté (coupure de courant probable)");
        if (i.EcartSecondes > 0) sb.Append($" : serveur indisponible environ {Duree(i.EcartSecondes)}");
        sb.Append('.');

        if (st.ServeurDemarreLe is { } demarre) sb.Append($" Redémarrage à {Heure(demarre)}.");

        if (i.SessionsFigees > 0)
        {
            sb.Append($" {Sessions(i.SessionsFigees)} payante(s) figée(s) au dernier signe de vie ({Montant(i.MontantFige)} Ar) : le temps de la coupure n'est pas facturé.");
        }
        else
        {
            sb.Append(" Aucune session payante n'était en cours.");
        }

        sb.Append(" Détails dans la page Incidents de l'interface web.");
        return sb.ToString();
    }

    public static string TexteIncidentCourt(StatutServeur st)
    {
        var i = st.Incident!;

        if (EstHorloge(i)) return "L'heure de l'ordinateur est suspecte : aucune session n'a été figée automatiquement.";

        return i.SessionsFigees > 0
            ? $"Arrêt anormal détecté. {Sessions(i.SessionsFigees)} payante(s) figée(s) ({Montant(i.MontantFige)} Ar)."
            : "Arrêt anormal détecté. Aucune session payante n'était en cours.";
    }

    public static string Sessions(long n) => n == 1 ? "1 session" : $"{n} sessions";

    public static string Montant(long ar) =>
        string.Format(CultureInfo.InvariantCulture, "{0:N0}", ar).Replace(',', ' ');

    public static string Heure(DateTimeOffset d) =>
        d.ToOffset(DecalageMadagascar).ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string DateHeure(DateTimeOffset d) =>
        d.ToOffset(DecalageMadagascar).ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);

    public static string Duree(long secondes)
    {
        if (secondes < 60) return $"{secondes} s";
        if (secondes < 3600) return $"{secondes / 60} min";

        if (secondes < 86400)
        {
            var h = secondes / 3600;
            var m = secondes % 3600 / 60;
            return m == 0 ? $"{h} h" : $"{h} h {m:00} min";
        }

        var j = secondes / 86400;
        var hh = secondes % 86400 / 3600;
        return hh == 0 ? $"{j} j" : $"{j} j {hh} h";
    }

    public static string DureeCourte(double secondes) => Duree((long)Math.Max(0, Math.Round(secondes)));

    public static string Taille(long mo)
    {
        if (mo >= 1024)
        {
            return (mo / 1024.0).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + " Go";
        }

        return mo + " Mo";
    }
}