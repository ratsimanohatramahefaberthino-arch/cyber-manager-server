namespace CyberManager.Server.Setup;

public enum ModeInstallation { Nouvelle, MiseAJour, Reparation, Retrogradation }

public enum EtatPort { Libre, Occupe, Reserve, Invalide }

/// <summary>Rapport complet de « detect ». Noms JSON en snake_case (ex. version_cible), enums en snake_case.</summary>
public sealed class RapportDetection
{
    public int V { get; set; } = 1;
    public DateTimeOffset GenereLe { get; set; } = DateTimeOffset.Now;
    public string VersionCible { get; set; } = "";

    /// <summary>Premier port libre de la liste (80 d'abord). Null : aucun libre, le moteur laissera le système choisir.</summary>
    public int? PortRecommande { get; set; }

    /// <summary>prefere (premier de la liste), repli, ou aucun.</summary>
    public string PortOrigine { get; set; } = "aucun";

    public InfoSysteme Systeme { get; set; } = new();
    public InfoInstallation Installation { get; set; } = new();
    public InfoReseau Reseau { get; set; } = new();
    public List<ResultatPort> Ports { get; set; } = new();
    public InfoXampp Xampp { get; set; } = new();

    /// <summary>Empêchent d'installer.</summary>
    public List<string> Bloquants { get; set; } = new();

    /// <summary>À lire avant de continuer, sans empêcher.</summary>
    public List<string> Avertissements { get; set; } = new();

    public List<string> Infos { get; set; } = new();
}

public sealed class RapportPorts
{
    public int V { get; set; } = 1;
    public int? PortRecommande { get; set; }
    public List<ResultatPort> Ports { get; set; } = new();
}

public sealed class InfoSysteme
{
    public string NomWindows { get; set; } = "";
    public int Build { get; set; }
    public bool Os64Bits { get; set; }
    public bool Administrateur { get; set; }
    public bool Supporte { get; set; }
    public string NomMachine { get; set; } = "";
    public string LecteurCode { get; set; } = "";
    public long? EspaceLibreCodeMo { get; set; }
    public string LecteurDonnees { get; set; } = "";
    public long? EspaceLibreDonneesMo { get; set; }
}

public sealed class InfoInstallation
{
    public ModeInstallation Mode { get; set; } = ModeInstallation.Nouvelle;
    public string Raison { get; set; } = "";
    public string VersionCible { get; set; } = "";
    public string? VersionCode { get; set; }
    public string? VersionMigree { get; set; }
    public bool ServiceExiste { get; set; }
    public string? ServiceEtat { get; set; }
    public bool ServiceAutomatique { get; set; }
    public bool CodePresent { get; set; }

    /// <summary>Vrai si cyber.sqlite ou le .env existent déjà dans ProgramData : on ne les recrée JAMAIS.</summary>
    public bool DonneesExistantes { get; set; }
    public bool BasePresente { get; set; }
    public bool EnvPresent { get; set; }
    public int? SessionsEnCours { get; set; }
}

public sealed class InfoReseau
{
    public List<InterfaceReseau> Interfaces { get; set; } = new();
    public int NombreEligibles { get; set; }
    public string? InterfaceRecommandee { get; set; }
    public string? AdresseRecommandee { get; set; }
}

public sealed class InterfaceReseau
{
    public string Nom { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>ethernet, wifi ou autre.</summary>
    public string Type { get; set; } = "autre";
    public bool Virtuelle { get; set; }
    public bool Eligible { get; set; }
    public bool Prive { get; set; }
    public bool Dhcp { get; set; }
    public string? Passerelle { get; set; }
    public List<AdresseIp> Adresses { get; set; } = new();
    public bool Recommandee { get; set; }
    public string Raison { get; set; } = "";
}

public sealed class AdresseIp
{
    public string Ip { get; set; } = "";
    public int Prefixe { get; set; }
}

public sealed class ResultatPort
{
    public int Port { get; set; }
    public EtatPort Etat { get; set; }
    public string Message { get; set; } = "";
    public string? Processus { get; set; }
    public int? Pid { get; set; }
    public string? Chemin { get; set; }

    /// <summary>libre, xampp_apache, apache, windows_http, mysql, autre.</summary>
    public string Categorie { get; set; } = "libre";
}

public sealed class InfoXampp
{
    public bool Detecte { get; set; }
    public bool ApacheActif { get; set; }
    public bool MysqlActif { get; set; }
    public bool MysqlEnEcoute { get; set; }
    public List<InstallationXampp> Installations { get; set; } = new();
    public List<ServiceDetecte> Services { get; set; } = new();
    public List<string> Processus { get; set; } = new();
}

public sealed class InstallationXampp
{
    public string Chemin { get; set; } = "";
    public bool Apache { get; set; }
    public bool Mysql { get; set; }
    public int? PortMysql { get; set; }
    public string? DossierDonnees { get; set; }

    /// <summary>Bases contenant la table cyber_sessions (candidates pour l'import à l'étape 4.4).</summary>
    public List<string> BasesCyber { get; set; } = new();
}

public sealed class ServiceDetecte
{
    public string Nom { get; set; } = "";
    public string Etat { get; set; } = "";
}