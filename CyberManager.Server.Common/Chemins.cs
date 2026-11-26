namespace CyberManager.Server.Common;

/// <summary>
/// Emplacements Windows du serveur.
///   Code (lecture seule) : %ProgramFiles%\CyberManager\
///   Données (écriture)   : %ProgramData%\CyberManager\  (config, data, logs, runtime, etat, ...)
/// Variables de surcharge (développement / tests) :
///   CYBER_CODE_DIR, CYBER_APP_DIR, CYBER_DONNEES_DIR (même nom que côté Laravel),
///   CYBER_PHP_CLI, CYBER_PHP_CGI, CYBER_CADDY.
/// </summary>
public static class Chemins
{
    private static string? Env(string nom)
    {
        var v = Environment.GetEnvironmentVariable(nom);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim().Trim('"').TrimEnd('\\', '/');
    }

    // ------------------------------------------------------------------
    // Code (lecture seule)
    // ------------------------------------------------------------------

    public static string Code =>
        Env("CYBER_CODE_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CyberManager");

    /// <summary>Dossier de l'application Laravel (celui qui contient « artisan »).</summary>
    public static string App
    {
        get
        {
            var force = Env("CYBER_APP_DIR");
            if (force != null) return force;

            var code = Code;
            return File.Exists(Path.Combine(code, "artisan")) ? code : Path.Combine(code, "app");
        }
    }

    public static string Artisan => Path.Combine(App, "artisan");

    /// <summary>Cherche runtime\&lt;relatif&gt; dans le dossier du bundle puis dans celui de l'application.</summary>
    private static string ResoudreRuntime(string relatif)
    {
        var candidats = new[]
        {
            Path.Combine(Code, "runtime", relatif),
            Path.Combine(App, "runtime", relatif)
        };

        foreach (var c in candidats)
        {
            if (File.Exists(c)) return c;
        }

        return candidats[0];
    }

    public static string PhpCli => Env("CYBER_PHP_CLI") ?? ResoudreRuntime(Path.Combine("php", "php.exe"));
    public static string PhpCgi => Env("CYBER_PHP_CGI") ?? ResoudreRuntime(Path.Combine("php", "php-cgi.exe"));
    public static string Caddy => Env("CYBER_CADDY") ?? ResoudreRuntime(Path.Combine("caddy", "caddy.exe"));

    /// <summary>Version de l'application (fichier VERSION à la racine du code), ou null.</summary>
    public static string? LireVersionApplication()
    {
        try
        {
            var f = Path.Combine(Code, "VERSION");
            if (File.Exists(f))
            {
                var t = File.ReadAllText(f).Trim();
                return t.Length > 0 ? t : null;
            }
        }
        catch
        {
            // illisible : on retombe sur la version de l'hôte
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Données (écriture)
    // ------------------------------------------------------------------

    public static string Donnees =>
        Env("CYBER_DONNEES_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CyberManager");

    public static string Config => Path.Combine(Donnees, "config");
    public static string Data => Path.Combine(Donnees, "data");
    public static string Logs => Path.Combine(Donnees, "logs");
    public static string Runtime => Path.Combine(Donnees, "runtime");
    public static string Etat => Path.Combine(Donnees, "etat");
    public static string Storage => Path.Combine(Donnees, "storage");
    public static string Cache => Path.Combine(Donnees, "cache");
    public static string Temp => Path.Combine(Donnees, "temp");
    public static string Sauvegardes => Path.Combine(Donnees, "sauvegardes");

    // Fichiers
    public static string ServeurJson => Path.Combine(Config, "serveur.json");
    public static string EnvLaravel => Path.Combine(Config, ".env");
    public static string BaseSqlite => Path.Combine(Data, "cyber.sqlite");
    public static string PortsJson => Path.Combine(Runtime, "ports.json");
    public static string Caddyfile => Path.Combine(Runtime, "Caddyfile");
    public static string FlagArretPropre => Path.Combine(Etat, "arret_propre.flag");
    public static string VersionInstallee => Path.Combine(Etat, "version_installee.txt");

    /// <summary>Crée tous les dossiers de données (y compris l'arborescence storage/ exigée par Laravel).</summary>
    public static void AssurerDossiers()
    {
        var dossiers = new[]
        {
            Donnees, Config, Data, Logs, Runtime, Etat, Cache, Temp, Sauvegardes,
            Path.Combine(Runtime, "caddy-config"),
            Path.Combine(Runtime, "caddy-data"),
            Storage,
            Path.Combine(Storage, "app", "public"),
            Path.Combine(Storage, "framework", "cache", "data"),
            Path.Combine(Storage, "framework", "sessions"),
            Path.Combine(Storage, "framework", "views"),
            Path.Combine(Storage, "framework", "testing"),
            Path.Combine(Storage, "logs")
        };

        foreach (var d in dossiers)
        {
            Directory.CreateDirectory(d);
        }
    }
}