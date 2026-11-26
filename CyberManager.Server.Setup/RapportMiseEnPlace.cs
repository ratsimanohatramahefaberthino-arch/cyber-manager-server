using System.Text.Json;
using System.Text.Json.Serialization;

namespace CyberManager.Server.Setup;

public static class Statut
{
    public const string Ok = "ok";
    public const string Conserve = "conserve";
    public const string Ignore = "ignore";
    public const string Avertissement = "avertissement";
    public const string Echec = "echec";
    public const string NonExecute = "non_execute";
}

public sealed class EtapeResultat
{
    public string Nom { get; set; } = "";
    public string Statut { get; set; } = Setup.Statut.Ok;
    public string Detail { get; set; } = "";
    public long DureeMs { get; set; }

    /// <summary>Avertissements remontés dans le rapport global (non sérialisé ici).</summary>
    [JsonIgnore] public List<string> Alertes { get; } = new();

    public static EtapeResultat Reussie(string detail) => new() { Statut = Setup.Statut.Ok, Detail = detail };
    public static EtapeResultat Gardee(string detail) => new() { Statut = Setup.Statut.Conserve, Detail = detail };
    public static EtapeResultat Ignoree(string detail) => new() { Statut = Setup.Statut.Ignore, Detail = detail };
    public static EtapeResultat Echouee(string detail) => new() { Statut = Setup.Statut.Echec, Detail = detail };
}

/// <summary>Rapport de « mise-en-place ». Ne contient JAMAIS le mot de passe.</summary>
public sealed class RapportMiseEnPlace
{
    public int V { get; set; } = 1;
    public DateTimeOffset GenereLe { get; set; } = DateTimeOffset.Now;
    public bool Ok { get; set; }
    public ModeInstallation Mode { get; set; } = ModeInstallation.Nouvelle;
    public string VersionCible { get; set; } = "";

    /// <summary>creee, completee, existante, incomplete (réponse de serveur:installer).</summary>
    public string? Base { get; set; }

    /// <summary>cree, conserve, absent.</summary>
    public string? Admin { get; set; }

    public int? PortHttp { get; set; }
    public string? CodeInstallation { get; set; }
    public string? Journal { get; set; }

    public List<EtapeResultat> Etapes { get; set; } = new();
    public List<string> Erreurs { get; set; } = new();
    public List<string> Avertissements { get; set; } = new();
}

/// <summary>Extrait le dernier objet JSON d'une sortie console (PHP peut afficher des avertissements avant).</summary>
internal static class JsonDernierObjet
{
    public static T? Lire<T>(string texte) where T : class
    {
        var lignes = texte.Split('\n');

        for (var i = lignes.Length - 1; i >= 0; i--)
        {
            var l = lignes[i].Trim();
            if (l.Length < 2 || l[0] != '{' || l[^1] != '}') continue;

            try
            {
                return JsonSerializer.Deserialize<T>(l, Sortie.Options);
            }
            catch (JsonException)
            {
                // ligne qui ressemblait à du JSON : on remonte
            }
        }

        return null;
    }
}