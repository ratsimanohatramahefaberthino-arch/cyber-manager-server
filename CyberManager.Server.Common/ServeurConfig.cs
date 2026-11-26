using System.Text.Json;
using System.Text.Json.Serialization;

namespace CyberManager.Server.Common;

/// <summary>
/// config\serveur.json : lu par le service (jamais écrit par lui).
/// Les autres clés du fichier (interface, port souhaité...) sont ignorées ici : elles concernent Laravel.
/// </summary>
public sealed class ServeurConfig
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;

    /// <summary>Code d'installation des postes, s'il est stocké ici par l'installateur.</summary>
    [JsonPropertyName("code_installation")] public string? CodeInstallation { get; set; }

    /// <summary>Attente maximale de la synchronisation de l'heure Windows (10 à 300 s, défaut 120).</summary>
    [JsonPropertyName("attente_horloge_secondes")] public int AttenteHorlogeSecondes { get; set; } = 120;

    /// <summary>Attente maximale du premier test de santé (20 à 300 s, défaut 60).</summary>
    [JsonPropertyName("attente_sante_secondes")] public int AttenteSanteSecondes { get; set; } = 60;

    /// <summary>Échecs consécutifs avant l'état EN_PANNE d'un processus (3 à 50, défaut 8).</summary>
    [JsonPropertyName("redemarrages_max")] public int RedemarragesMax { get; set; } = 8;

    /// <summary>Période du contrôle de santé (5 à 60 s, défaut 10).</summary>
    [JsonPropertyName("intervalle_sante_secondes")] public int IntervalleSanteSecondes { get; set; } = 10;

    /// <summary>Retard du planificateur déclenchant son redémarrage (15 à 300 s, défaut 30).</summary>
    [JsonPropertyName("retard_planificateur_secondes")] public int RetardPlanificateurSecondes { get; set; } = 30;

    public static ServeurConfig Charger(string chemin, out string? erreur)
    {
        erreur = null;
        ServeurConfig config;

        try
        {
            config = File.Exists(chemin)
                ? JsonSerializer.Deserialize<ServeurConfig>(File.ReadAllText(chemin), JsonUtil.Options) ?? new ServeurConfig()
                : new ServeurConfig();
        }
        catch (Exception ex)
        {
            erreur = $"serveur.json illisible, valeurs par défaut utilisées : {ex.Message}";
            config = new ServeurConfig();
        }

        config.Normaliser();
        return config;
    }

    private void Normaliser()
    {
        AttenteHorlogeSecondes = Math.Clamp(AttenteHorlogeSecondes, 10, 300);
        AttenteSanteSecondes = Math.Clamp(AttenteSanteSecondes, 20, 300);
        RedemarragesMax = Math.Clamp(RedemarragesMax, 3, 50);
        IntervalleSanteSecondes = Math.Clamp(IntervalleSanteSecondes, 5, 60);
        RetardPlanificateurSecondes = Math.Clamp(RetardPlanificateurSecondes, 15, 300);
    }
}