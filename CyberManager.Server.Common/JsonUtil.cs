using System.Text.Json;
using System.Text.Json.Serialization;

namespace CyberManager.Server.Common;

public static class JsonUtil
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Extrait le dernier objet JSON d'une sortie console (PHP peut afficher des avertissements avant le JSON).
    /// </summary>
    public static string? ExtraireDernierObjet(string? sortie)
    {
        if (string.IsNullOrWhiteSpace(sortie)) return null;

        var lignes = sortie.Split('\n');
        for (var i = lignes.Length - 1; i >= 0; i--)
        {
            var l = lignes[i].Trim();
            if (l.StartsWith('{') && l.EndsWith('}')) return l;
        }

        var debut = sortie.IndexOf('{');
        var fin = sortie.LastIndexOf('}');
        return (debut >= 0 && fin > debut) ? sortie.Substring(debut, fin - debut + 1) : null;
    }

    public static T? LireDernierObjet<T>(string? sortie) where T : class
    {
        var json = ExtraireDernierObjet(sortie);
        if (json == null) return null;

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Lit une liste de chaînes que PHP peut avoir encodée en tableau [..] OU en objet {..}
/// (json_encode d'un tableau associatif) : dans les deux cas, on récupère les valeurs texte.
/// </summary>
public sealed class ListeChainesTolerante : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var resultat = new List<string>();

        switch (reader.TokenType)
        {
            case JsonTokenType.StartArray:
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        resultat.Add(reader.GetString() ?? "");
                    }
                    else if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
                    {
                        reader.Skip();
                    }
                }
                break;

            case JsonTokenType.StartObject:
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    // jeton courant : nom de propriété ; on lit sa valeur
                    if (!reader.Read()) break;

                    if (reader.TokenType == JsonTokenType.String)
                    {
                        resultat.Add(reader.GetString() ?? "");
                    }
                    else if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
                    {
                        reader.Skip();
                    }
                }
                break;

            case JsonTokenType.Null:
                break;

            default:
                reader.Skip();
                break;
        }

        return resultat;
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var s in value)
        {
            writer.WriteStringValue(s);
        }
        writer.WriteEndArray();
    }
}