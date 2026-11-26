using System.Text.Json;
using System.Text.Json.Serialization;

namespace CyberManager.Server.Control;

/// <summary>Préférences locales de la télécommande (propres à l'utilisateur Windows).</summary>
internal sealed class ParametresControle
{
    /// <summary>Clé du dernier incident que le gérant a acquitté (bouton « Compris »).</summary>
    [JsonPropertyName("incident_acquitte")] public string? IncidentAcquitte { get; set; }

    private static string Fichier => Path.Combine(JournalControle.Dossier, "controle.json");

    public static ParametresControle Charger()
    {
        try
        {
            if (File.Exists(Fichier))
            {
                return JsonSerializer.Deserialize<ParametresControle>(File.ReadAllText(Fichier)) ?? new ParametresControle();
            }
        }
        catch (Exception ex)
        {
            JournalControle.Ecrire("Préférences illisibles : " + ex.Message);
        }

        return new ParametresControle();
    }

    public void Enregistrer()
    {
        try
        {
            Directory.CreateDirectory(JournalControle.Dossier);
            File.WriteAllText(Fichier, JsonSerializer.Serialize(this));
        }
        catch (Exception ex)
        {
            JournalControle.Ecrire("Préférences non enregistrées : " + ex.Message);
        }
    }
}