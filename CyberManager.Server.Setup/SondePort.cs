using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CyberManager.Server.Setup;

/// <summary>
/// Sonde un port par TEST RÉEL, comme ChoixPorts (PHP) : quelqu'un écoute-t-il déjà ? peut-on écouter ?
/// Si le port est pris, on dit QUI (netstat, indépendant de la langue de Windows).
/// Le port n'est jamais gardé : l'écoute d'essai est fermée immédiatement.
/// </summary>
internal static class SondePort
{
    private static readonly Lazy<List<(int Port, int Pid)>> Table = new(LireTable);

    public static List<ResultatPort> SonderListe(IEnumerable<int> ports) => ports.Select(Sonder).ToList();

    public static ResultatPort Sonder(int port)
    {
        if (port < 1 || port > 65535)
        {
            return new ResultatPort
            {
                Port = port, Etat = EtatPort.Invalide, Categorie = "invalide",
                Message = $"Le port {port} n'est pas valide (entier de 1 à 65535)."
            };
        }

        if (EstEnEcoute(port))
        {
            var pid = Table.Value.Where(t => t.Port == port).Select(t => t.Pid).FirstOrDefault();
            return Occupant(port, pid);
        }

        try
        {
            var ecoute = new TcpListener(IPAddress.Any, port) { ExclusiveAddressUse = true };
            ecoute.Start();
            ecoute.Stop();

            return new ResultatPort { Port = port, Etat = EtatPort.Libre, Message = $"Le port {port} est libre.", Categorie = "libre" };
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            return Occupant(port, 0);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
        {
            return new ResultatPort
            {
                Port = port, Etat = EtatPort.Reserve, Categorie = "autre",
                Message = $"Le port {port} est réservé par Windows (Hyper-V, Docker…) : choisissez-en un autre."
            };
        }
        catch (Exception ex)
        {
            return new ResultatPort
            {
                Port = port, Etat = EtatPort.Reserve, Categorie = "autre",
                Message = $"Le port {port} ne peut pas être utilisé : {ex.Message}"
            };
        }
    }

    internal static bool EstEnEcoute(int port)
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => e.Port == port);
        }
        catch
        {
            return false;
        }
    }

    private static ResultatPort Occupant(int port, int pid)
    {
        string? nom = null, chemin = null;

        if (pid > 0)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                nom = p.ProcessName;
                try { chemin = p.MainModule?.FileName; } catch { /* accès refusé (processus système) */ }
            }
            catch
            {
                // le processus vient de disparaître
            }
        }

        var categorie = Categorie(pid, nom, chemin);
        var qui = categorie switch
        {
            "xampp_apache" => "Apache de XAMPP",
            "apache" => "un serveur Apache",
            "windows_http" => "un service web de Windows (IIS ou équivalent)",
            "mysql" => "MySQL / MariaDB",
            _ => nom != null ? $"le programme « {nom} »" : "un autre programme"
        };

        return new ResultatPort
        {
            Port = port,
            Etat = EtatPort.Occupe,
            Message = $"Le port {port} est déjà utilisé par {qui}.",
            Processus = nom,
            Pid = pid > 0 ? pid : null,
            Chemin = chemin,
            Categorie = categorie
        };
    }

    private static string Categorie(int pid, string? nom, string? chemin)
    {
        var n = (nom ?? "").ToLowerInvariant();
        var c = (chemin ?? "").ToLowerInvariant();

        if (pid == 4 || n is "system" or "w3wp" or "inetinfo") return "windows_http";
        if (n == "httpd") return c.Contains("xampp") ? "xampp_apache" : "apache";
        if (n is "mysqld" or "mariadbd") return "mysql";
        return "autre";
    }

    /// <summary>Lit « netstat -ano » sans dépendre de la langue : une ligne TCP d'écoute a un pair distant en « :0 ».</summary>
    private static List<(int Port, int Pid)> LireTable()
    {
        var res = new List<(int, int)>();

        try
        {
            var psi = new ProcessStartInfo("netstat.exe", "-ano")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };

            using var p = Process.Start(psi);
            if (p == null) return res;

            var sortie = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);

            foreach (var ligne in sortie.Split('\n'))
            {
                var c = ligne.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (c.Length < 5 || c[0] != "TCP" || !c[2].EndsWith(":0", StringComparison.Ordinal)) continue;

                var i = c[1].LastIndexOf(':');
                if (i < 0 || !int.TryParse(c[1][(i + 1)..], out var port)) continue;
                if (!int.TryParse(c[^1], out var pid)) continue;

                res.Add((port, pid));
            }
        }
        catch
        {
            // sans netstat on dira « un autre programme » : le test d'écoute reste fiable
        }

        return res;
    }
}