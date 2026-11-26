using System.Text.Json.Serialization;

namespace CyberManager.Server.Common.Ipc;

/// <summary>États exposés par le service (chaînes exactes du protocole).</summary>
public static class EtatServeur
{
    public const string Demarrage = "DEMARRAGE";
    public const string Pret = "PRET";
    public const string Degrade = "DEGRADE";
    public const string Arrete = "ARRETE";
    public const string EnPanne = "EN_PANNE";
    public const string BaseARestaurer = "BASE_A_RESTAURER";
    public const string HorlogeAVerifier = "HORLOGE_A_VERIFIER";

    public static readonly string[] Tous =
    {
        Demarrage, Pret, Degrade, Arrete, EnPanne, BaseARestaurer, HorlogeAVerifier
    };
}

/// <summary>Commandes du pipe (version 1).</summary>
public static class Commandes
{
    public const string GetStatus = "GetStatus";
    public const string Start = "Start";
    public const string Stop = "Stop";
    public const string Restart = "Restart";
    public const string BackupNow = "BackupNow";
    public const string GetInstallCode = "GetInstallCode";
}

public static class CodesErreur
{
    public const string RequeteInvalide = "REQUETE_INVALIDE";
    public const string VersionNonSupportee = "VERSION_NON_SUPPORTEE";
    public const string CommandeInconnue = "COMMANDE_INCONNUE";
    public const string ConfirmationRequise = "CONFIRMATION_REQUISE";
    public const string OperationEnCours = "OPERATION_EN_COURS";
    public const string ServiceOccupe = "SERVICE_OCCUPE";
    public const string Echec = "ECHEC";
    public const string CodeIntrouvable = "CODE_INTROUVABLE";
    public const string ErreurInterne = "ERREUR_INTERNE";
}

public static class TypeProcessus
{
    public const string Caddy = "caddy";
    public const string Worker = "worker";
    public const string Planificateur = "planificateur";
    public const string Decouverte = "decouverte";
}

public static class PhaseProcessus
{
    public const string EnCours = "EN_COURS";
    public const string Temporisation = "TEMPORISATION";
    public const string EnPanne = "EN_PANNE";
    public const string Arrete = "ARRETE";
}

/// <summary>Une requête = une ligne JSON : {"v":1,"cmd":"GetStatus"}</summary>
public sealed class PipeRequete
{
    [JsonPropertyName("v")] public int V { get; set; } = Constantes.VersionProtocole;
    [JsonPropertyName("cmd")] public string? Cmd { get; set; }

    /// <summary>Vrai si l'utilisateur a confirmé l'action dans la télécommande (arrêt avec sessions en cours...).</summary>
    [JsonPropertyName("confirmer")] public bool Confirmer { get; set; }

    /// <summary>Identifiant de corrélation, renvoyé tel quel dans la réponse.</summary>
    [JsonPropertyName("id")] public string? Id { get; set; }
}

/// <summary>Une réponse = une ligne JSON.</summary>
public sealed class PipeReponse
{
    [JsonPropertyName("v")] public int V { get; set; } = Constantes.VersionProtocole;
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("cmd")] public string? Cmd { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }

    /// <summary>Renseigné pour CONFIRMATION_REQUISE : nombre de sessions payantes qui seraient figées.</summary>
    [JsonPropertyName("sessions_en_cours")] public int? SessionsEnCours { get; set; }

    [JsonPropertyName("statut")] public StatutServeur? Statut { get; set; }
    [JsonPropertyName("code_installation")] public string? CodeInstallation { get; set; }
    [JsonPropertyName("details")] public string? Details { get; set; }
}

public sealed class ProcessusStatut
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("nom")] public string Nom { get; set; } = "";
    [JsonPropertyName("port")] public int? Port { get; set; }
    [JsonPropertyName("pid")] public int? Pid { get; set; }
    [JsonPropertyName("vivant")] public bool Vivant { get; set; }
    [JsonPropertyName("phase")] public string Phase { get; set; } = PhaseProcessus.Arrete;
    [JsonPropertyName("redemarrages")] public int Redemarrages { get; set; }
    [JsonPropertyName("detail")] public string? Detail { get; set; }
}

public sealed class IncidentCoupure
{
    [JsonPropertyName("id")] public int? Id { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("sessions_figees")] public int SessionsFigees { get; set; }
    [JsonPropertyName("montant_fige")] public long MontantFige { get; set; }
    [JsonPropertyName("ecart_secondes")] public long EcartSecondes { get; set; }
}

public sealed class StatutServeur
{
    [JsonPropertyName("etat")] public string Etat { get; set; } = EtatServeur.Demarrage;
    [JsonPropertyName("message")] public string Message { get; set; } = "";

    [JsonPropertyName("version_hote")] public string VersionHote { get; set; } = Constantes.VersionHote;
    [JsonPropertyName("version_application")] public string? VersionApplication { get; set; }

    [JsonPropertyName("service_demarre_le")] public DateTimeOffset ServiceDemarreLe { get; set; }
    [JsonPropertyName("serveur_demarre_le")] public DateTimeOffset? ServeurDemarreLe { get; set; }
    [JsonPropertyName("duree_fonctionnement_secondes")] public long DureeFonctionnementSecondes { get; set; }

    [JsonPropertyName("port_http")] public int? PortHttp { get; set; }
    [JsonPropertyName("port_udp_decouverte")] public int? PortUdpDecouverte { get; set; }
    [JsonPropertyName("urls")] public List<string> Urls { get; set; } = new();

    [JsonPropertyName("caddy")] public ProcessusStatut? Caddy { get; set; }
    [JsonPropertyName("workers")] public List<ProcessusStatut> Workers { get; set; } = new();
    [JsonPropertyName("workers_actifs")] public int WorkersActifs { get; set; }
    [JsonPropertyName("workers_total")] public int WorkersTotal { get; set; }
    [JsonPropertyName("planificateur")] public ProcessusStatut? Planificateur { get; set; }
    [JsonPropertyName("decouverte")] public ProcessusStatut? Decouverte { get; set; }

    /// <summary>Âge du dernier pouls du planificateur (serveur.alive_at), en secondes.</summary>
    [JsonPropertyName("planificateur_dernier_passage_secondes")] public double? PlanificateurDernierPassageSecondes { get; set; }

    [JsonPropertyName("base_ok")] public bool BaseOk { get; set; } = true;
    [JsonPropertyName("sessions_en_cours")] public int? SessionsEnCours { get; set; }
    [JsonPropertyName("horloge_bloquee")] public bool HorlogeBloquee { get; set; }

    /// <summary>Incident de coupure détecté au dernier démarrage (renseigné si une coupure ou une horloge suspecte a été traitée).</summary>
    [JsonPropertyName("incident")] public IncidentCoupure? Incident { get; set; }

    [JsonPropertyName("avertissements")] public List<string> Avertissements { get; set; } = new();
    [JsonPropertyName("derniere_sauvegarde")] public DateTimeOffset? DerniereSauvegarde { get; set; }
    [JsonPropertyName("espace_disque_libre_mo")] public long? EspaceDisqueLibreMo { get; set; }
}