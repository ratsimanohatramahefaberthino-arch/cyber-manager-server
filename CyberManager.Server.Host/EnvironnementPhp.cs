using System.Diagnostics;
using CyberManager.Server.Common;

namespace CyberManager.Server.Host;

/// <summary>
/// Variables d'environnement imposées à TOUS les processus PHP et à Caddy (§2.2.3 du cahier) :
/// Laravel est en lecture seule dans Program Files et écrit dans ProgramData.
/// Ces variables priment sur le .env (le dotenv de Laravel ne les écrase pas).
/// </summary>
internal static class EnvironnementPhp
{
    public static void Appliquer(ProcessStartInfo psi, IDictionary<string, string>? supplement = null)
    {
        var env = psi.Environment;

        env["CYBER_DONNEES_DIR"] = Chemins.Donnees;
        env["CYBER_CADDY"] = Chemins.Caddy;
        env["CYBER_PHP_CGI"] = Chemins.PhpCgi;
        env["CYBER_SERVICE_HOTE"] = "1";

        env["DB_CONNECTION"] = "sqlite";
        env["DB_DATABASE"] = Chemins.BaseSqlite;
        env["APP_STORAGE_PATH"] = Chemins.Storage;

        env["APP_SERVICES_CACHE"] = Path.Combine(Chemins.Cache, "services.php");
        env["APP_PACKAGES_CACHE"] = Path.Combine(Chemins.Cache, "packages.php");
        env["APP_CONFIG_CACHE"] = Path.Combine(Chemins.Cache, "config.php");
        env["APP_ROUTES_CACHE"] = Path.Combine(Chemins.Cache, "routes-v7.php");
        env["APP_EVENTS_CACHE"] = Path.Combine(Chemins.Cache, "events.php");

        // Le compte de service n'a pas forcément de dossier temporaire utilisable
        env["TMP"] = Chemins.Temp;
        env["TEMP"] = Chemins.Temp;

        // php.ini : celui de ProgramData\...\runtime s'il existe, sinon celui du dossier de PHP
        var dossierPhp = Path.GetDirectoryName(Chemins.PhpCgi) ?? "";
        env["PHPRC"] = File.Exists(Path.Combine(Chemins.Runtime, "php.ini")) ? Chemins.Runtime : dossierPhp;

        // Caddy (admin off) : évite toute dépendance au profil utilisateur (%AppData%) du compte de service
        env["XDG_CONFIG_HOME"] = Path.Combine(Chemins.Runtime, "caddy-config");
        env["XDG_DATA_HOME"] = Path.Combine(Chemins.Runtime, "caddy-data");

        // DLL des extensions PHP
        var pathActuel = env.TryGetValue("PATH", out var p) ? p : "";
        env["PATH"] = dossierPhp + ";" + pathActuel;

        if (supplement != null)
        {
            foreach (var (cle, valeur) in supplement)
            {
                env[cle] = valeur;
            }
        }
    }
}