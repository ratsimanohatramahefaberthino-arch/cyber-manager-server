using System.Diagnostics;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using CyberManager.Server.Common;
using Microsoft.Win32;

namespace CyberManager.Server.Setup;

/// <summary>
/// Service Windows, tâches héritées, arrêt et démarrage.
///
/// Arrêt : TOUJOURS gracieux (SCM). Le service écrit alors arret_propre = vrai. Un arrêt brutal serait
/// interprété au redémarrage comme une coupure de courant (incident, sessions figées) : on ne tue jamais.
/// </summary>
internal static class EtapeService
{
    private static readonly string Compte = @"NT SERVICE\" + Constantes.NomService;
    private static readonly TimeSpan DelaiArret = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan DelaiDemarrage = TimeSpan.FromSeconds(60);

    // ------------------------------------------------------------------
    // Enregistrement
    // ------------------------------------------------------------------

    public static EtapeResultat Installer()
    {
        var exe = Disposition.ServiceExe;
        if (!File.Exists(exe)) return EtapeResultat.Echouee($"Le programme du service est introuvable : {exe} (installation incomplète).");

        var existait = Existe();
        var binPath = "\"" + exe + "\"";   // guillemets conservés : pas de chemin « non quoté » (faille classique des services)

        var principal = existait
            ? Sc("config", Constantes.NomService, "binPath=", binPath, "start=", "auto", "obj=", Compte, "DisplayName=", Constantes.NomAffiche)
            : Sc("create", Constantes.NomService, "binPath=", binPath, "type=", "own", "start=", "auto", "obj=", Compte, "DisplayName=", Constantes.NomAffiche);

        if (!principal.Ok)
        {
            var raison = principal.Code == 1072
                ? "Un ancien service est en cours de suppression : redémarrez Windows puis relancez l'installation."
                : principal.Extrait();
            return EtapeResultat.Echouee("Enregistrement du service impossible : " + raison);
        }

        var details = new[]
        {
            Sc("description", Constantes.NomService, "Supervise le serveur Cyber Manager (serveur web, PHP, planificateur, découverte des postes)."),
            // Relance automatique : 5 s, 15 s puis 60 s ; compteur remis à zéro au bout d'une journée
            Sc("failure", Constantes.NomService, "reset=", "86400", "actions=", "restart/5000/restart/15000/restart/60000"),
            // Relancer aussi quand le service s'arrête avec un code d'erreur (erreur fatale gérée par le service lui-même)
            Sc("failureflag", Constantes.NomService, "1"),
            Sc("sidtype", Constantes.NomService, "unrestricted")
        };

        var echec = details.FirstOrDefault(r => !r.Ok);
        if (echec != null) return EtapeResultat.Echouee("Réglage du service impossible : " + echec.Extrait());

        var verification = Verifier(binPath);
        if (verification != null) return EtapeResultat.Echouee(verification);

        var etape = EtapeResultat.Reussie(
            (existait ? "Service reconfiguré" : "Service créé")
            + " : démarrage automatique, compte virtuel « " + Compte + " », relance après plantage à 5, 15 puis 60 secondes.");

        var sidAttendu = SidServiceVirtuel.Pour(Constantes.NomService).Value;
        var sid = Sc("showsid", Constantes.NomService);
        var m = Regex.Match(sid.Sortie, @"S-1-5-80(-\d+){5}");
        if (m.Success && !m.Value.Equals(sidAttendu, StringComparison.OrdinalIgnoreCase))
        {
            etape.Alertes.Add($"Le SID réel du service ({m.Value}) diffère de celui des droits NTFS ({sidAttendu}) : le service risque de ne pas pouvoir écrire ses données.");
        }

        return etape;
    }

    /// <summary>Relit la configuration dans le registre (indépendant de la langue de Windows).</summary>
    private static string? Verifier(string binPath)
    {
        try
        {
            using var cle = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + Constantes.NomService);
            if (cle == null) return "Le service n'apparaît pas dans le registre après sa création.";

            var image = (cle.GetValue("ImagePath") as string ?? "").Trim();
            if (!image.Equals(binPath, StringComparison.OrdinalIgnoreCase))
            {
                return $"Chemin du service inattendu : {image}";
            }

            var compte = cle.GetValue("ObjectName") as string ?? "";
            if (!compte.Equals(Compte, StringComparison.OrdinalIgnoreCase))
            {
                return $"Compte du service inattendu : {compte} (attendu : {Compte}).";
            }

            if (cle.GetValue("Start") is not int demarrage || demarrage != 2)
            {
                return "Le service n'est pas en démarrage automatique.";
            }

            if (cle.GetValue("DelayedAutostart") is int differe && differe != 0)
            {
                return "Le démarrage du service est différé : il doit être automatique et immédiat.";
            }

            if (cle.GetValue("FailureActions") is not byte[] { Length: > 0 })
            {
                return "La stratégie de relance après plantage n'a pas été enregistrée.";
            }

            return null;
        }
        catch (Exception ex)
        {
            return "Vérification du service impossible : " + ex.Message;
        }
    }

    // ------------------------------------------------------------------
    // Arrêt / démarrage
    // ------------------------------------------------------------------

    public static EtapeResultat Arreter()
    {
        using var sc = Obtenir();
        if (sc == null) return EtapeResultat.Ignoree("Le service n'est pas installé : rien à arrêter.");

        try
        {
            sc.Refresh();

            if (sc.Status != ServiceControllerStatus.Stopped)
            {
                if (sc.Status == ServiceControllerStatus.StartPending)
                {
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                }

                if (sc.Status != ServiceControllerStatus.StopPending)
                {
                    sc.Stop();
                }

                sc.WaitForStatus(ServiceControllerStatus.Stopped, DelaiArret);
            }
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            return EtapeResultat.Echouee("Le service ne s'est pas arrêté dans le délai de 90 secondes. Ne le forcez pas : "
                                         + "un arrêt brutal serait pris pour une coupure de courant. Réessayez dans un instant.");
        }
        catch (Exception ex)
        {
            return EtapeResultat.Echouee("Arrêt du service impossible : " + ex.Message);
        }

        var etape = EtapeResultat.Reussie("Service arrêté proprement (aucun incident de coupure ne sera déclaré).");

        var verrous = ProcessusDansLeCode();
        if (verrous.Count > 0)
        {
            etape.Alertes.Add("Des programmes du dossier d'installation sont encore actifs : " + string.Join(", ", verrous)
                              + ". Ils pourraient bloquer le remplacement des fichiers.");
        }

        return etape;
    }

    public static EtapeResultat Demarrer()
    {
        using var sc = Obtenir();
        if (sc == null) return EtapeResultat.Echouee("Le service Cyber Manager n'est pas installé.");

        try
        {
            sc.Refresh();

            if (sc.Status == ServiceControllerStatus.Running)
            {
                return EtapeResultat.Gardee("Le service est déjà démarré.");
            }

            if (sc.Status != ServiceControllerStatus.StartPending) sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, DelaiDemarrage);
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            return EtapeResultat.Echouee("Le service n'a pas démarré dans le délai de 60 secondes.");
        }
        catch (Exception ex)
        {
            return EtapeResultat.Echouee("Démarrage du service impossible : " + ex.Message);
        }

        return EtapeResultat.Reussie("Service démarré. La séquence de démarrage complète (base, ports, serveur web) se poursuit : l'auto-test la vérifiera.");
    }

    // ------------------------------------------------------------------
    // Anciennes tâches planifiées (scripts de développement)
    // ------------------------------------------------------------------

    public static EtapeResultat NettoyerTachesHeritees()
    {
        var schtasks = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
        var retirees = new List<string>();

        foreach (var nom in Disposition.TachesHeritees)
        {
            var existe = Execution.Lancer(schtasks, new[] { "/Query", "/TN", nom }, TimeSpan.FromSeconds(20));
            if (!existe.Ok) continue;

            // Arrête d'abord l'écoute UDP éventuellement en cours, sinon notre service ne pourrait pas lier le port
            Execution.Lancer(schtasks, new[] { "/End", "/TN", nom }, TimeSpan.FromSeconds(20));

            var suppression = Execution.Lancer(schtasks, new[] { "/Delete", "/TN", nom, "/F" }, TimeSpan.FromSeconds(20));
            if (suppression.Ok) retirees.Add(nom);
            else return EtapeResultat.Echouee($"La tâche « {nom} » n'a pas pu être supprimée : {suppression.Extrait()}");
        }

        return retirees.Count == 0
            ? EtapeResultat.Ignoree("Aucune ancienne tâche planifiée trouvée.")
            : EtapeResultat.Reussie("Anciennes tâches planifiées retirées : " + string.Join(", ", retirees) + ".");
    }

    // ------------------------------------------------------------------
    // Outils
    // ------------------------------------------------------------------

    public static bool Existe()
    {
        using var sc = Obtenir();
        return sc != null;
    }

    private static ServiceController? Obtenir()
    {
        var sc = new ServiceController(Constantes.NomService);

        try
        {
            _ = sc.Status;
            return sc;
        }
        catch (InvalidOperationException)
        {
            sc.Dispose();
            return null;   // le service n'existe pas
        }
    }

    private static ResultatExecution Sc(params string[] arguments) =>
        Execution.Lancer(Path.Combine(Environment.SystemDirectory, "sc.exe"), arguments, TimeSpan.FromSeconds(30));

    private static List<string> ProcessusDansLeCode()
    {
        var res = new List<string>();
        var racine = Chemins.Code.TrimEnd('\\') + "\\";
        var moi = Environment.ProcessId;

        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id == moi) continue;

                    var chemin = p.MainModule?.FileName;
                    if (chemin != null && chemin.StartsWith(racine, StringComparison.OrdinalIgnoreCase))
                    {
                        res.Add($"{p.ProcessName} (PID {p.Id})");
                    }
                }
                catch
                {
                    // processus système ou terminé : non lisible, sans importance
                }
            }
        }

        return res;
    }
}