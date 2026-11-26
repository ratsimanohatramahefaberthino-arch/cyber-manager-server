namespace CyberManager.Server.Common;

public static class Constantes
{
    /// <summary>Nom du service Windows (compte virtuel : NT SERVICE\CyberManagerServer).</summary>
    public const string NomService = "CyberManagerServer";

    public const string NomAffiche = "Cyber Manager Server";

    /// <summary>Pipe local : \\.\pipe\CyberManagerServer</summary>
    public const string NomPipe = "CyberManagerServer";

    /// <summary>Mutex global d'instance unique du service.</summary>
    public const string NomMutex = @"Global\CyberManagerServerService";

    /// <summary>Version du protocole JSON du pipe.</summary>
    public const int VersionProtocole = 1;

    /// <summary>Version du service hôte (utilisée aussi comme version applicative si le fichier VERSION est absent).</summary>
    public const string VersionHote = "3.1.0";
}