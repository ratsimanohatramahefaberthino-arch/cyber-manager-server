using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Win32;

namespace CyberManager.Server.Setup;

/// <summary>
/// Détecte un ancien XAMPP (Règle 3 : « serveur avec un ancien XAMPP présent ») : dossiers sur tous les disques fixes,
/// chemins lus dans les services Windows (XAMPP peut être en E:\xampp), processus actifs, et bases contenant cyber_sessions.
/// Lecture seule : rien n'est jamais arrêté ni modifié ici.
/// </summary>
internal static class DetectionXampp
{
    private const string TableSignature = "cyber_sessions";

    public static InfoXampp Detecter()
    {
        var info = new InfoXampp();

        var racines = new List<string>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType == DriveType.Fixed && d.IsReady) racines.Add(Path.Combine(d.RootDirectory.FullName, "xampp"));
            }
            catch
            {
                // lecteur illisible
            }
        }

        racines.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "xampp"));
        racines.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "xampp"));

        var noms = new List<string>();
        info.Services = LireServices(noms);
        racines.AddRange(RacinesDepuisServices(noms));

        foreach (var racine in racines.Select(r => r.TrimEnd('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var inst = Analyser(racine);
            if (inst != null) info.Installations.Add(inst);
        }

        foreach (var nom in new[] { "httpd", "mysqld", "mariadbd", "xampp-control" })
        {
            foreach (var p in Process.GetProcessesByName(nom))
            {
                using (p)
                {
                    info.Processus.Add($"{p.ProcessName} (PID {p.Id})");
                }
            }
        }

        info.ApacheActif = info.Processus.Any(p => p.StartsWith("httpd", StringComparison.Ordinal))
                           || info.Services.Any(s => s.Nom.StartsWith("Apache", StringComparison.OrdinalIgnoreCase) && s.Etat == "Running");
        info.MysqlActif = info.Processus.Any(p => p.StartsWith("mysqld", StringComparison.Ordinal) || p.StartsWith("mariadbd", StringComparison.Ordinal))
                          || info.Services.Any(s => !s.Nom.StartsWith("Apache", StringComparison.OrdinalIgnoreCase) && s.Etat == "Running");

        var portsMysql = info.Installations.Select(i => i.PortMysql ?? 3306).Append(3306).Distinct();
        info.MysqlEnEcoute = portsMysql.Any(SondePort.EstEnEcoute);

        info.Detecte = info.Installations.Count > 0;
        return info;
    }

    private static InstallationXampp? Analyser(string racine)
    {
        if (!Directory.Exists(racine)) return null;

        var apache = File.Exists(Path.Combine(racine, "apache", "bin", "httpd.exe"));
        var mysql = File.Exists(Path.Combine(racine, "mysql", "bin", "mysqld.exe"));
        if (!apache && !mysql && !File.Exists(Path.Combine(racine, "xampp-control.exe"))) return null;

        var (port, donnees) = LireMyIni(racine);
        donnees ??= Path.Combine(racine, "mysql", "data");

        var inst = new InstallationXampp
        {
            Chemin = racine,
            Apache = apache,
            Mysql = mysql,
            PortMysql = port ?? 3306,
            DossierDonnees = Directory.Exists(donnees) ? donnees : null
        };

        if (inst.DossierDonnees != null)
        {
            try
            {
                foreach (var dossier in Directory.GetDirectories(inst.DossierDonnees))
                {
                    if (File.Exists(Path.Combine(dossier, TableSignature + ".ibd"))
                        || File.Exists(Path.Combine(dossier, TableSignature + ".frm")))
                    {
                        inst.BasesCyber.Add(Path.GetFileName(dossier));
                    }
                }
            }
            catch
            {
                // dossier de données illisible : pas de base candidate
            }
        }

        return inst;
    }

    /// <summary>Lit port= et datadir= dans la section [mysqld] de my.ini.</summary>
    private static (int? Port, string? Donnees) LireMyIni(string racine)
    {
        int? port = null;
        string? donnees = null;

        try
        {
            var fichier = Path.Combine(racine, "mysql", "bin", "my.ini");
            if (!File.Exists(fichier)) return (null, null);

            var dansMysqld = false;
            foreach (var brute in File.ReadLines(fichier))
            {
                var ligne = brute.Trim();
                if (ligne.Length == 0 || ligne[0] is '#' or ';') continue;

                if (ligne[0] == '[')
                {
                    dansMysqld = ligne.Equals("[mysqld]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!dansMysqld) continue;

                var egal = ligne.IndexOf('=');
                if (egal <= 0) continue;

                var cle = ligne[..egal].Trim().ToLowerInvariant();
                var valeur = ligne[(egal + 1)..].Trim().Trim('"');

                if (cle == "port" && port == null && int.TryParse(valeur, out var p)) port = p;
                if (cle == "datadir" && donnees == null) donnees = valeur.Replace('/', '\\');
            }
        }
        catch
        {
            // my.ini illisible : valeurs par défaut
        }

        return (port, donnees);
    }

    private static List<ServiceDetecte> LireServices(List<string> noms)
    {
        var res = new List<ServiceDetecte>();

        try
        {
            foreach (var s in ServiceController.GetServices())
            {
                using (s)
                {
                    var n = s.ServiceName.ToLowerInvariant();
                    if (n.StartsWith("apache", StringComparison.Ordinal) || n.StartsWith("mysql", StringComparison.Ordinal) || n.Contains("mariadb"))
                    {
                        res.Add(new ServiceDetecte { Nom = s.ServiceName, Etat = s.Status.ToString() });
                        noms.Add(s.ServiceName);
                    }
                }
            }
        }
        catch
        {
            // liste des services illisible
        }

        return res;
    }

    /// <summary>ImagePath d'un service Apache/MySQL : « "E:\xampp\apache\bin\httpd.exe" -k runservice » → E:\xampp.</summary>
    private static IEnumerable<string> RacinesDepuisServices(IEnumerable<string> noms)
    {
        foreach (var nom in noms)
        {
            string? image = null;

            try
            {
                using var cle = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + nom);
                image = cle?.GetValue("ImagePath") as string;
            }
            catch
            {
                // clé inaccessible
            }

            if (string.IsNullOrWhiteSpace(image)) continue;

            image = image.Trim().TrimStart('"');

            foreach (var marqueur in new[] { @"\apache\bin", @"\mysql\bin" })
            {
                var i = image.IndexOf(marqueur, StringComparison.OrdinalIgnoreCase);
                if (i > 0)
                {
                    yield return image[..i];
                    break;
                }
            }
        }
    }
}