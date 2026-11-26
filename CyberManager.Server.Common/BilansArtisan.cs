using System.Text.Json;
using System.Text.Json.Serialization;

namespace CyberManager.Server.Common;

/// <summary>Sortie de « php artisan serveur:preparer --json ».</summary>
public sealed class PreparationBilan
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("simulation")] public bool Simulation { get; set; }

    [JsonPropertyName("erreurs")]
    [JsonConverter(typeof(ListeChainesTolerante))]
    public List<string> Erreurs { get; set; } = new();

    [JsonPropertyName("avertissements")]
    [JsonConverter(typeof(ListeChainesTolerante))]
    public List<string> Avertissements { get; set; } = new();

    /// <summary>Détail des tests (objet, ou [] si vide côté PHP) : conservé tel quel pour le diagnostic.</summary>
    [JsonPropertyName("tests")] public JsonElement Tests { get; set; }

    [JsonPropertyName("etat")] public PortsJson? Etat { get; set; }
    [JsonPropertyName("modifie")] public bool? Modifie { get; set; }
    [JsonPropertyName("caddyfile_modifie")] public bool? CaddyfileModifie { get; set; }
    [JsonPropertyName("sauvegarde_caddyfile")] public string? SauvegardeCaddyfile { get; set; }
}

/// <summary>Sortie de « php artisan serveur:demarrage --json » (ResilienceService::traiterDemarrageServeur).</summary>
public sealed class DemarrageBilan
{
    /// <summary>normal, premier_demarrage, horloge_suspecte, coupure_serveur, arret_sans_session.</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("incident_id")] public int? IncidentId { get; set; }
    [JsonPropertyName("sessions_figees")] public int SessionsFigees { get; set; }
    [JsonPropertyName("montant_fige")] public long MontantFige { get; set; }
    [JsonPropertyName("ecart_secondes")] public long EcartSecondes { get; set; }
}