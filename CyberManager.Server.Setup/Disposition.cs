using CyberManager.Server.Common;

namespace CyberManager.Server.Setup;

/// <summary>Où l'installateur place chaque composant dans Program Files\CyberManager (code, lecture seule).</summary>
internal static class Disposition
{
    public const string NomRegleWeb = "Cyber Manager - Serveur web (Caddy)";
    public const string NomRegleDecouverte = "Cyber Manager - Découverte des postes (UDP)";
    public const int PortUdpDefaut = 47474;

    /// <summary>Tâches planifiées de l'ancien déploiement (scripts de développement) : à retirer, elles occuperaient le port UDP.</summary>
    public static readonly string[] TachesHeritees = { "CyberManager Decouverte", "CyberManager Sauvegarde" };

    /// <summary>CYBER_SERVICE_EXE permet de tester sur un poste de développement sans passer par Program Files.</summary>
    public static string ServiceExe =>
        Environment.GetEnvironmentVariable("CYBER_SERVICE_EXE") is { Length: > 0 } force
            ? force.Trim().Trim('"')
            : Path.Combine(Chemins.Code, "service", "CyberManagerServerService.exe");

    public static string ControleExe => Path.Combine(Chemins.Code, "control", "CyberManagerServer.exe");

    public static string InstallateurExe => Path.Combine(Chemins.Code, "installer", "CyberManagerServerSetup.exe");
}