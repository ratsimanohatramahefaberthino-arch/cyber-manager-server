using System.Text.Json;
using System.Text.Json.Serialization;

namespace CyberManager.Server.Common;

/// <summary>Contenu de ports.json, écrit par « php artisan serveur:preparer » (EtatMoteur::VERSION = 1).</summary>
public sealed class PortsJson
{
    [JsonPropertyName("version")] public int Version { get; set; }
    [JsonPropertyName("genere_le")] public string? GenereLe { get; set; }
    [JsonPropertyName("http_port")] public int HttpPort { get; set; }
    [JsonPropertyName("http_origine")] public string? HttpOrigine { get; set; }
    [JsonPropertyName("fastcgi_ports")] public List<int> FastcgiPorts { get; set; } = new();
    [JsonPropertyName("workers")] public List<WorkerPhp> Workers { get; set; } = new();

    /// <summary>Brut : PHP encode un tableau associatif vide en [] au lieu de {}. Utiliser EnvWorkers().</summary>
    [JsonPropertyName("env_workers")] public JsonElement EnvWorkersBrut { get; set; }

    [JsonPropertyName("caddy")] public string? Caddy { get; set; }
    [JsonPropertyName("php_cgi")] public string? PhpCgi { get; set; }
    [JsonPropertyName("php_cli")] public string? PhpCli { get; set; }
    [JsonPropertyName("caddyfile")] public string? Caddyfile { get; set; }
    [JsonPropertyName("caddyfile_sha256")] public string? CaddyfileSha256 { get; set; }
    [JsonPropertyName("racine_publique")] public string? RacinePublique { get; set; }
    [JsonPropertyName("dossier_logs")] public string? DossierLogs { get; set; }

    [JsonPropertyName("interfaces")]
    [JsonConverter(typeof(ListeChainesTolerante))]
    public List<string> Interfaces { get; set; } = new();

    [JsonPropertyName("urls")]
    [JsonConverter(typeof(ListeChainesTolerante))]
    public List<string> Urls { get; set; } = new();

    [JsonPropertyName("decouverte")] public DecouvertePorts Decouverte { get; set; } = new();

    public Dictionary<string, string> EnvWorkers()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (EnvWorkersBrut.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in EnvWorkersBrut.EnumerateObject())
            {
                d[p.Name] = p.Value.ValueKind == JsonValueKind.String ? (p.Value.GetString() ?? "") : p.Value.ToString();
            }
        }

        return d;
    }
}

public sealed class WorkerPhp
{
    [JsonPropertyName("port")] public int Port { get; set; }

    /// <summary>[php-cgi.exe, -b, 127.0.0.1:PORT]</summary>
    [JsonPropertyName("commande")]
    [JsonConverter(typeof(ListeChainesTolerante))]
    public List<string> Commande { get; set; } = new();
}

public sealed class DecouvertePorts
{
    [JsonPropertyName("udp_port")] public int UdpPort { get; set; }
    [JsonPropertyName("http_port")] public int HttpPort { get; set; }

    /// <summary>[artisan, cyber:decouverte, --http-port=.., --udp-port=..]</summary>
    [JsonPropertyName("arguments")]
    [JsonConverter(typeof(ListeChainesTolerante))]
    public List<string> Arguments { get; set; } = new();
}

public static class LecteurPorts
{
    public const int VersionAttendue = 1;

    /// <summary>Lit et valide ports.json ; null si absent, illisible ou incohérent.</summary>
    public static PortsJson? Lire(string chemin)
    {
        try
        {
            if (!File.Exists(chemin)) return null;

            var p = JsonSerializer.Deserialize<PortsJson>(File.ReadAllText(chemin), JsonUtil.Options);
            return (p != null && Valider(p, out _)) ? p : null;
        }
        catch
        {
            return null;
        }
    }

    public static bool Valider(PortsJson p, out string raison)
    {
        if (p.Version != VersionAttendue)
        {
            raison = $"version de ports.json inattendue ({p.Version})";
            return false;
        }

        if (p.HttpPort is < 1 or > 65535)
        {
            raison = "port HTTP invalide";
            return false;
        }

        if (p.Workers.Count == 0)
        {
            raison = "aucun worker php-cgi défini";
            return false;
        }

        foreach (var w in p.Workers)
        {
            if (w.Port is < 1 or > 65535 || w.Commande.Count == 0)
            {
                raison = "worker php-cgi invalide";
                return false;
            }
        }

        raison = "";
        return true;
    }
}