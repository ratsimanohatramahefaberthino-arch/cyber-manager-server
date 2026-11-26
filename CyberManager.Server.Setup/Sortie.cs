using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CyberManager.Server.Setup;

/// <summary>
/// Deux formats de sortie :
///  - json : pour les tests, les scripts, la télécommande ;
///  - ini  : pour Inno Setup (Pascal sans JSON).
/// Encodage de l'INI (option --encodage) :
///  - utf16 (défaut) : UTF-16 LE avec BOM, lu par l'API Windows GetPrivateProfileString ;
///  - utf8           : UTF-8 avec BOM, que le Pascal d'Inno décode sûrement (MultiByteToWideChar).
/// </summary>
internal static class Sortie
{
    /// <summary>utf16 ou utf8 (renseigné par Program.Main d'après --encodage).</summary>
    public static string EncodageIni { get; set; } = "utf16";

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public static string Json(object o) => JsonSerializer.Serialize(o, o.GetType(), Options);

    public static string Ini(object o) =>
        IniWriter.Construire(JsonSerializer.SerializeToElement(o, o.GetType(), Options));

    public static void Emettre(object rapport, string format, string? fichier)
    {
        var ini = format == "ini";
        var texte = ini ? Ini(rapport) : Json(rapport);

        if (string.IsNullOrWhiteSpace(fichier))
        {
            Console.Out.Write(texte);
            Console.Out.WriteLine();
            return;
        }

        var dossier = Path.GetDirectoryName(Path.GetFullPath(fichier));
        if (!string.IsNullOrEmpty(dossier)) Directory.CreateDirectory(dossier);

        byte[] octets;
        if (ini)
        {
            Encoding enc = EncodageIni == "utf8" ? new UTF8Encoding(true) : new UnicodeEncoding(false, true);
            octets = enc.GetPreamble().Concat(enc.GetBytes(texte)).ToArray();
        }
        else
        {
            octets = new UTF8Encoding(false).GetBytes(texte);
        }

        var temporaire = fichier + ".tmp";
        File.WriteAllBytes(temporaire, octets);
        File.Move(temporaire, fichier, true);
    }
}

/// <summary>
/// Aplatit le JSON en INI : un objet de premier niveau = une section ; les tableaux deviennent
/// cle_count, cle_1, cle_2... ; les objets imbriqués deviennent parent_enfant. Booléens : 1 / 0.
/// Les scalaires de premier niveau vont dans [meta].
/// </summary>
internal static class IniWriter
{
    public static string Construire(JsonElement racine)
    {
        var meta = new List<KeyValuePair<string, string>>();
        var sections = new List<(string Nom, List<KeyValuePair<string, string>> Cles)>();

        foreach (var p in racine.EnumerateObject())
        {
            if (p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                var liste = new List<KeyValuePair<string, string>>();
                Aplatir("", p.Value, liste);
                sections.Add((p.Name, liste));
            }
            else
            {
                Aplatir(p.Name, p.Value, meta);
            }
        }

        var sb = new StringBuilder();
        Ecrire(sb, "meta", meta);
        foreach (var (nom, cles) in sections) Ecrire(sb, nom, cles);
        return sb.ToString();
    }

    private static void Ecrire(StringBuilder sb, string nom, List<KeyValuePair<string, string>> cles)
    {
        sb.Append('[').Append(nom).Append("]\r\n");
        foreach (var c in cles) sb.Append(c.Key).Append('=').Append(c.Value).Append("\r\n");
        sb.Append("\r\n");
    }

    private static string Cle(string prefixe, string nom) => prefixe.Length == 0 ? nom : prefixe + "_" + nom;

    private static void Aplatir(string prefixe, JsonElement e, List<KeyValuePair<string, string>> sortie)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject()) Aplatir(Cle(prefixe, p.Name), p.Value, sortie);
                break;

            case JsonValueKind.Array:
                sortie.Add(new(Cle(prefixe, "count"), e.GetArrayLength().ToString()));
                var n = 0;
                foreach (var item in e.EnumerateArray())
                {
                    n++;
                    Aplatir(Cle(prefixe, n.ToString()), item, sortie);
                }
                break;

            case JsonValueKind.String:
                sortie.Add(new(prefixe, Nettoyer(e.GetString() ?? "")));
                break;

            case JsonValueKind.True:
                sortie.Add(new(prefixe, "1"));
                break;

            case JsonValueKind.False:
                sortie.Add(new(prefixe, "0"));
                break;

            case JsonValueKind.Number:
                sortie.Add(new(prefixe, e.GetRawText()));
                break;

            default:
                sortie.Add(new(prefixe, ""));
                break;
        }
    }

    private static string Nettoyer(string s) => s.Replace("\r", " ").Replace("\n", " ").Trim();
}