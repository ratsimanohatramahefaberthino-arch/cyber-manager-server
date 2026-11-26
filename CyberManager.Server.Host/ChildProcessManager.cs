using System.Diagnostics;
using System.Text;
using CyberManager.Server.Common;
using CyberManager.Server.Common.Ipc;

namespace CyberManager.Server.Host;

internal enum PhaseEnfant
{
    Arrete,
    EnCours,
    Temporisation,
    EnPanne
}

internal sealed class ProcessusEnfant
{
    public required string Nom { get; init; }
    public required string Type { get; init; }
    public required string FichierJournal { get; init; }
    public int? Port { get; init; }
    public required Func<ProcessStartInfo> Fabrique { get; init; }

    public Process? Processus { get; set; }
    public PhaseEnfant Phase { get; set; } = PhaseEnfant.Arrete;
    public int Echecs { get; set; }
    public int Redemarrages { get; set; }
    public DateTime? DebutUtc { get; set; }
    public DateTime ProchainEssaiUtc { get; set; }
    public string? Message { get; set; }

    public bool Vivant
    {
        get
        {
            try { return Processus != null && !Processus.HasExited; }
            catch { return false; }
        }
    }

    public int? Pid
    {
        get
        {
            try { return Processus?.Id; }
            catch { return null; }
        }
    }
}

/// <summary>
/// Lance et supervise Caddy, les workers php-cgi, schedule:work et cyber:decouverte :
/// sans fenêtre, rattachés au Job Object, relancés avec temporisation croissante.
/// Après N échecs consécutifs (démarrage immédiat raté, ou plantages en moins de 60 s) : EN_PANNE, plus de relance.
/// Un php-cgi qui se termine proprement après PHP_FCGI_MAX_REQUESTS requêtes (durée de vie ≥ 60 s) est simplement relancé.
/// </summary>
internal sealed class ChildProcessManager
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly TimeSpan VieStable = TimeSpan.FromSeconds(60);

    private readonly JobObject _job;
    private readonly object _verrou = new();
    private readonly List<ProcessusEnfant> _enfants = new();
    private bool _actif;

    public int RedemarragesMax { get; set; } = 8;

    public ChildProcessManager(JobObject job)
    {
        _job = job;
    }

    // ------------------------------------------------------------------
    // Configuration (d'après ports.json)
    // ------------------------------------------------------------------

    /// <summary>Ordre de démarrage : workers → Caddy → planificateur → découverte (arrêt dans l'ordre inverse).</summary>
    public void Configurer(PortsJson ports)
    {
        lock (_verrou)
        {
            _enfants.Clear();

            var envWorkers = ports.EnvWorkers();
            var phpCgi = string.IsNullOrWhiteSpace(ports.PhpCgi) ? Chemins.PhpCgi : ports.PhpCgi!;
            var phpCli = string.IsNullOrWhiteSpace(ports.PhpCli) ? Chemins.PhpCli : ports.PhpCli!;
            var caddy = string.IsNullOrWhiteSpace(ports.Caddy) ? Chemins.Caddy : ports.Caddy!;
            var caddyfile = string.IsNullOrWhiteSpace(ports.Caddyfile) ? Chemins.Caddyfile : ports.Caddyfile!;
            var racine = string.IsNullOrWhiteSpace(ports.RacinePublique) ? Path.Combine(Chemins.App, "public") : ports.RacinePublique!;

            foreach (var w in ports.Workers)
            {
                var exe = string.IsNullOrWhiteSpace(w.Commande[0]) ? phpCgi : w.Commande[0];
                var args = w.Commande.Skip(1).ToList();
                var port = w.Port;

                _enfants.Add(new ProcessusEnfant
                {
                    Nom = $"php-cgi:{port}",
                    Type = TypeProcessus.Worker,
                    FichierJournal = "php-cgi",
                    Port = port,
                    Fabrique = () => Info(exe, args, racine, envWorkers)
                });
            }

            _enfants.Add(new ProcessusEnfant
            {
                Nom = "caddy",
                Type = TypeProcessus.Caddy,
                FichierJournal = "caddy",
                Port = ports.HttpPort,
                Fabrique = () => Info(caddy, new[] { "run", "--config", caddyfile, "--adapter", "caddyfile" }, Chemins.Runtime, null)
            });

            _enfants.Add(new ProcessusEnfant
            {
                Nom = "schedule:work",
                Type = TypeProcessus.Planificateur,
                FichierJournal = "planificateur",
                Fabrique = () => Info(phpCli, new[] { "artisan", "schedule:work" }, Chemins.App, null)
            });

            var argsDecouverte = ports.Decouverte.Arguments.Count > 0
                ? ports.Decouverte.Arguments.ToList()
                : new List<string> { "artisan", "cyber:decouverte", $"--http-port={ports.HttpPort}", $"--udp-port={ports.Decouverte.UdpPort}" };

            _enfants.Add(new ProcessusEnfant
            {
                Nom = "cyber:decouverte",
                Type = TypeProcessus.Decouverte,
                FichierJournal = "decouverte",
                Port = ports.Decouverte.UdpPort > 0 ? ports.Decouverte.UdpPort : null,
                Fabrique = () => Info(phpCli, argsDecouverte, Chemins.App, null)
            });
        }
    }

    private static ProcessStartInfo Info(string exe, IEnumerable<string> arguments, string dossier, IDictionary<string, string>? supplement)
    {
        if (!File.Exists(exe))
        {
            throw new FileNotFoundException($"Programme introuvable : {exe}");
        }

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Directory.Exists(dossier) ? dossier : Chemins.App,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8
        };

        foreach (var a in arguments)
        {
            psi.ArgumentList.Add(a);
        }

        EnvironnementPhp.Appliquer(psi, supplement);
        return psi;
    }

    // ------------------------------------------------------------------
    // Démarrage / arrêt
    // ------------------------------------------------------------------

    public void DemarrerTous()
    {
        lock (_verrou)
        {
            _actif = true;
            foreach (var e in _enfants)
            {
                e.Echecs = 0;
                e.Redemarrages = 0;
                Lancer(e);
            }
        }
    }

    /// <summary>Arrêt dans l'ordre inverse du démarrage. Plus aucune relance ensuite.</summary>
    public void ArreterTous()
    {
        lock (_verrou)
        {
            _actif = false;

            for (var i = _enfants.Count - 1; i >= 0; i--)
            {
                var e = _enfants[i];
                if (e.Processus != null)
                {
                    Journal.Ecrire($"Arrêt : {e.Nom}");
                }

                Tuer(e);
                e.Phase = PhaseEnfant.Arrete;
            }
        }
    }

    private void Lancer(ProcessusEnfant e)
    {
        try
        {
            var psi = e.Fabrique();
            var p = new Process { StartInfo = psi };

            p.OutputDataReceived += (_, a) => JournalEnfant.Ecrire(e.FichierJournal, e.Nom, a.Data);
            p.ErrorDataReceived += (_, a) => JournalEnfant.Ecrire(e.FichierJournal, e.Nom, a.Data == null ? null : "[err] " + a.Data);

            p.Start();

            try
            {
                _job.Assigner(p);
            }
            catch (Exception ex)
            {
                Journal.Ecrire($"ATTENTION : {e.Nom} non rattaché au Job Object : {ex.Message}");
            }

            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            e.Processus = p;
            e.Phase = PhaseEnfant.EnCours;
            e.DebutUtc = DateTime.UtcNow;
            e.Message = null;

            Journal.Ecrire($"Lancé : {e.Nom} (PID {p.Id})");
        }
        catch (Exception ex)
        {
            Journal.Ecrire($"Lancement impossible de {e.Nom} : {ex.Message}");
            Echec(e, "Lancement impossible : " + ex.Message, TimeSpan.Zero);
        }
    }

    private void Echec(ProcessusEnfant e, string message, TimeSpan vie)
    {
        e.Message = message;

        int delaiSecondes;
        if (vie >= VieStable)
        {
            e.Echecs = 0;          // longue vie : arrêt « normal » (recyclage php-cgi) ou plantage isolé
            delaiSecondes = 0;
        }
        else
        {
            e.Echecs++;
            delaiSecondes = Temporisation(e.Echecs);
        }

        if (e.Echecs >= RedemarragesMax)
        {
            e.Phase = PhaseEnfant.EnPanne;
            Journal.Ecrire($"{e.Nom} : {e.Echecs} échecs consécutifs, abandon des relances (EN_PANNE). Dernier message : {message}");
            return;
        }

        e.Phase = PhaseEnfant.Temporisation;
        e.ProchainEssaiUtc = DateTime.UtcNow.AddSeconds(delaiSecondes);
        if (delaiSecondes > 0)
        {
            Journal.Ecrire($"{e.Nom} : nouvel essai dans {delaiSecondes} s (échec {e.Echecs}/{RedemarragesMax}).");
        }
    }

    /// <summary>0, 1, 2, 4, 8, 16, 30, 30... secondes.</summary>
    private static int Temporisation(int echecs) => echecs <= 0 ? 0 : Math.Min(30, 1 << Math.Min(echecs - 1, 5));

    private static void Tuer(ProcessusEnfant e)
    {
        var p = e.Processus;
        if (p == null) return;

        try
        {
            if (!p.HasExited)
            {
                p.Kill(true);
                p.WaitForExit(3000);
            }
        }
        catch
        {
            // déjà terminé
        }

        try { p.Dispose(); } catch { }
        e.Processus = null;
    }

    // ------------------------------------------------------------------
    // Supervision (appelée toutes les secondes)
    // ------------------------------------------------------------------

    public void Surveiller()
    {
        lock (_verrou)
        {
            if (!_actif) return;

            foreach (var e in _enfants)
            {
                if (e.Phase == PhaseEnfant.EnCours && !e.Vivant)
                {
                    int? code = null;
                    try { code = e.Processus?.ExitCode; } catch { }

                    var vie = e.DebutUtc.HasValue ? DateTime.UtcNow - e.DebutUtc.Value : TimeSpan.Zero;

                    try { e.Processus?.Dispose(); } catch { }
                    e.Processus = null;

                    Journal.Ecrire($"Arrêt inattendu de {e.Nom} (code {(code?.ToString() ?? "?")}) après {vie.TotalSeconds:F0} s.");
                    Echec(e, $"Arrêté (code {(code?.ToString() ?? "?")})", vie);
                }
                else if (e.Phase == PhaseEnfant.Temporisation && DateTime.UtcNow >= e.ProchainEssaiUtc)
                {
                    e.Redemarrages++;
                    Lancer(e);
                }
            }
        }
    }

    /// <summary>Redémarrage voulu (santé dégradée) : n'est pas compté comme un échec.</summary>
    public void Redemarrer(string type, string raison)
    {
        lock (_verrou)
        {
            if (!_actif) return;

            foreach (var e in _enfants.Where(x => x.Type == type && x.Phase == PhaseEnfant.EnCours))
            {
                Journal.Ecrire($"Redémarrage de {e.Nom} : {raison}");
                Tuer(e);
                e.Phase = PhaseEnfant.Temporisation;
                e.ProchainEssaiUtc = DateTime.UtcNow;
            }
        }
    }

    // ------------------------------------------------------------------
    // Interrogation
    // ------------------------------------------------------------------

/// <summary>Seul un arrêt complet de Caddy ou de tous les PHP constitue une panne critique.</summary>
    public string? PanneCritique()
    {
        lock (_verrou)
        {
            var caddy = _enfants.FirstOrDefault(e => e.Type == TypeProcessus.Caddy && e.Phase == PhaseEnfant.EnPanne);
            if (caddy != null) return $"Le serveur web (Caddy) ne démarre plus : {caddy.Message}";

            var workers = _enfants.Where(e => e.Type == TypeProcessus.Worker).ToList();
            if (workers.Count > 0 && workers.All(w => w.Phase == PhaseEnfant.EnPanne))
            {
                return "Tous les processus PHP sont en panne.";
            }

            return null;
        }
    }

    /// <summary>Anomalies non fatales (le planificateur ou la découverte ne bloquent pas les encaissements).</summary>
    public List<string> Degradations()
    {
        lock (_verrou)
        {
            var liste = new List<string>();

            var plan = _enfants.FirstOrDefault(e => e.Type == TypeProcessus.Planificateur && e.Phase == PhaseEnfant.EnPanne);
            if (plan != null)
            {
                liste.Add("Le planificateur d'arrière-plan redémarre.");
            }

            var workers = _enfants.Where(e => e.Type == TypeProcessus.Worker).ToList();
            var manquants = workers.Count(w => w.Phase != PhaseEnfant.EnCours);
            if (manquants > 0)
            {
                liste.Add($"{manquants} processus PHP sur {workers.Count} indisponible(s).");
            }

            var caddy = _enfants.FirstOrDefault(e => e.Type == TypeProcessus.Caddy);
            if (caddy != null && caddy.Phase != PhaseEnfant.EnCours && caddy.Phase != PhaseEnfant.EnPanne)
            {
                liste.Add("Le serveur web redémarre.");
            }

            var decouverte = _enfants.FirstOrDefault(e => e.Type == TypeProcessus.Decouverte);
            if (decouverte != null && decouverte.Phase != PhaseEnfant.EnCours)
            {
                liste.Add("La découverte réseau est inactive.");
            }

            return liste;
        }
    }

    /// <summary>Secondes écoulées depuis le lancement du premier enfant de ce type (0 s'il ne tourne pas).</summary>
    public double SecondesDepuisLancement(string type)
    {
        lock (_verrou)
        {
            var e = _enfants.FirstOrDefault(x => x.Type == type && x.Phase == PhaseEnfant.EnCours && x.DebutUtc.HasValue);
            return e == null ? 0 : (DateTime.UtcNow - e.DebutUtc!.Value).TotalSeconds;
        }
    }

    public List<ProcessusStatut> Instantane()
    {
        lock (_verrou)
        {
            return _enfants.Select(e => new ProcessusStatut
            {
                Type = e.Type,
                Nom = e.Nom,
                Port = e.Port,
                Pid = e.Vivant ? e.Pid : null,
                Vivant = e.Vivant,
                Phase = e.Phase switch
                {
                    PhaseEnfant.EnCours => PhaseProcessus.EnCours,
                    PhaseEnfant.Temporisation => PhaseProcessus.Temporisation,
                    PhaseEnfant.EnPanne => PhaseProcessus.EnPanne,
                    _ => PhaseProcessus.Arrete
                },
                Redemarrages = e.Redemarrages,
                Detail = e.Message
            }).ToList();
        }
    }
}