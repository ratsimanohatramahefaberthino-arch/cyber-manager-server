using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CyberManager.Server.Common;

namespace CyberManager.Server.Setup;

/// <summary>
/// Génère config\.env (APP_KEY propre à ce serveur) et config\serveur.json.
/// RÈGLE ABSOLUE : un fichier qui existe n'est JAMAIS réécrit (mise à jour, réparation, réinstallation).
/// Perdre l'APP_KEY rendrait illisibles les données chiffrées : on ne régénère donc rien de ce qui existe.
/// </summary>
internal static class EtapeConfiguration
{
    public static EtapeResultat Executer(Demande d)
    {
        var morceaux = new List<string>();

        var cle = GenererCleApp();
        var envCree = EcrireSiAbsent(Chemins.EnvLaravel, ContenuEnv(cle, Chemins.BaseSqlite));
        cle = "";

        morceaux.Add(envCree ? ".env créé (clé de chiffrement propre à ce serveur)" : ".env existant conservé");

        var jsonCree = EcrireSiAbsent(Chemins.ServeurJson, ContenuServeurJson(d));
        morceaux.Add(jsonCree ? "serveur.json créé" : "serveur.json existant conservé");

        var detail = string.Join(" ; ", morceaux) + ".";
        return envCree || jsonCree ? EtapeResultat.Reussie(detail) : EtapeResultat.Gardee(detail);
    }

    /// <summary>« base64: » + 32 octets aléatoires : format attendu par Laravel pour AES-256-CBC.</summary>
    internal static string GenererCleApp() => "base64:" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    internal static string ContenuEnv(string cleApp, string baseSqlite)
    {
        // Barres obliques : valides pour PHP sous Windows et sans ambiguïté d'échappement dans un .env
        var chemin = baseSqlite.Replace('\\', '/');

        var lignes = new[]
        {
            "# Fichier généré par l'installateur de Cyber Manager.",
            "# NE PAS PARTAGER : il contient la clé de chiffrement de ce serveur.",
            "# Une mise à jour ou une réparation ne le réécrit jamais.",
            "APP_NAME=\"Cyber Manager\"",
            "APP_ENV=production",
            "APP_KEY=" + cleApp,
            "APP_DEBUG=false",
            "APP_URL=http://localhost",
            "APP_TIMEZONE=Indian/Antananarivo",
            "",
            "LOG_CHANNEL=daily",
            "LOG_LEVEL=warning",
            "LOG_DAILY_DAYS=14",
            "",
            "DB_CONNECTION=sqlite",
            "DB_DATABASE=\"" + chemin + "\"",
            "DB_FOREIGN_KEYS=true",
            "",
            "SESSION_DRIVER=file",
            "CACHE_STORE=file",
            "QUEUE_CONNECTION=sync",
            "BROADCAST_CONNECTION=log",
            "FILESYSTEM_DISK=local",
            "MAIL_MAILER=log",
            "BCRYPT_ROUNDS=12",
            ""
        };

        return string.Join("\n", lignes);
    }

    internal static string ContenuServeurJson(Demande d)
    {
        var contenu = new Dictionary<string, object?>
        {
            ["version"] = 1,
            ["installe_le"] = DateTimeOffset.Now.ToString("o"),
            ["version_installateur"] = Constantes.VersionHote,
            ["interface"] = string.IsNullOrWhiteSpace(d.Interface) ? null : d.Interface
        };

        return JsonSerializer.Serialize(contenu, new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>Écriture sans écrasement : fichier temporaire puis déplacement (échoue si la destination existe).</summary>
    internal static bool EcrireSiAbsent(string chemin, string contenu)
    {
        if (File.Exists(chemin)) return false;

        var dossier = Path.GetDirectoryName(chemin)!;
        Directory.CreateDirectory(dossier);

        var temporaire = Path.Combine(dossier, Path.GetFileName(chemin) + ".tmp-" + Guid.NewGuid().ToString("N")[..8]);
        File.WriteAllText(temporaire, contenu, new UTF8Encoding(false));

        try
        {
            File.Move(temporaire, chemin);   // sans overwrite : lève IOException si le fichier est apparu entre-temps
            return true;
        }
        catch (IOException) when (File.Exists(chemin))
        {
            File.Delete(temporaire);
            return false;
        }
    }
}