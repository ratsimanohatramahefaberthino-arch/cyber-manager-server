using System.Diagnostics;
using CyberManager.Server.Common;
using CyberManager.Server.Common.Ipc;
using Microsoft.Extensions.Hosting;

namespace CyberManager.Server.Host;

/// <summary>
/// CyberManagerServerService : séquence de démarrage ultra-rapide et tolérante aux pannes puis supervision.
/// </summary>
internal sealed class SupervisorWorker : BackgroundService, IControleServeur
{
    private enum Phase { Initiale, Demarrage, EnService, Arrete, EchecRetentable, Bloque }
    private enum Issue { Ok, Retentable, Bloque }

    private const int TentativesMax = 3;
    private static readonly TimeSpan DelaiRetentative = TimeSpan.FromSeconds(15);

    private readonly JobObject _job;
    private readonly ProcessRunner _runner;
    private readonly ClockGuard _horloge;
    private readonly DatabaseGuard _base;
    private readonly ChildProcessManager _enfants;
    private readonly HealthMonitor _sante;
    private readonly EtatSuperviseur _etat;
    private readonly PipeServer _pipe;
    private readonly IHostApplicationLifetime _vie;

    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly SemaphoreSlim _sauvegarde = new(1, 1);

    private volatile Phase _phase = Phase.Initiale;
    private ServeurConfig _config = new();
    private CancellationToken _arret;
    private Mutex? _mutex;
    private bool _possedeInstance;
    private int _tentatives;
    private DateTime _prochaineTentativeUtc = DateTime.MaxValue;

    public SupervisorWorker(
        JobObject job,
        ProcessRunner runner,
        ClockGuard horloge,
        DatabaseGuard baseGuard,
        ChildProcessManager enfants,
        HealthMonitor sante,
        EtatSuperviseur etat,
        PipeServer pipe,
        IHostApplicationLifetime vie)
    {
        _job = job;
        _runner = runner;
        _horloge = horloge;
        _base = baseGuard;
        _enfants = enfants;
        _sante = sante;
        _etat = etat;
        _pipe = pipe;
        _vie = vie;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _arret = stoppingToken;
        Journal.AfficherConsole = Environment.UserInteractive;
        Journal.Ecrire($">>> Service {Constantes.NomService} {Constantes.VersionHote} démarré.");

        // Nettoyage immédiat des processus fantômes pour libérer les ports
        NettoyerProcessusOrphelins();

        var arretPropre = true;

        try
        {
            if (!AcquerirMutex())
            {
                Journal.Ecrire("Une autre instance du service est déjà active : arrêt.");
                _vie.StopApplication();
                return;
            }

            _possedeInstance = true;
            Chemins.AssurerDossiers();

            _config = ServeurConfig.Charger(Chemins.ServeurJson, out var erreurConfig);
            if (erreurConfig != null) Journal.Ecrire(erreurConfig);
            _enfants.RedemarragesMax = _config.RedemarragesMax;

            _pipe.Controle = this;
            _pipe.Demarrer(stoppingToken);

            await _operation.WaitAsync(stoppingToken);
            try
            {
                await DemarrerInterneAsync(stoppingToken);
            }
            finally
            {
                _operation.Release();
            }

            await BoucleAsync(stoppingToken);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Journal.Ecrire($">>> ERREUR FATALE : {ex}");
            _etat.Fixer(EtatServeur.EnPanne, "Erreur fatale : " + ex.Message);
            arretPropre = false;
            throw;
        }
        finally
        {
            await ArretFinalAsync(arretPropre);
        }
    }

    /// <summary>Tue immédiatement tout caddy.exe ou php-cgi.exe orphelin d'une ancienne session.</summary>
    private static void NettoyerProcessusOrphelins()
    {
        foreach (var nom in new[] { "caddy", "php-cgi" })
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(nom))
                {
                    try { p.Kill(); p.WaitForExit(1000); } catch { } finally { p.Dispose(); }
                }
            }
            catch { }
        }
    }

    private bool AcquerirMutex()
    {
        try
        {
            _mutex = new Mutex(false, Constantes.NomMutex);
            try { return _mutex.WaitOne(0); }
            catch (AbandonedMutexException) { return true; }
        }
        catch { return false; }
    }

    private async Task ArretFinalAsync(bool propre)
    {
        if (!_possedeInstance) return;

        try { await ArreterInterneAsync(propre); } catch { }
        await _pipe.ArreterAsync();
        Journal.Ecrire(">>> Service arrêté.");
    }

    private async Task BoucleAsync(CancellationToken ct)
    {
        var prochaineSante = DateTime.UtcNow.AddSeconds(_config.IntervalleSanteSecondes);

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(1000, ct);

            if (_phase == Phase.EnService)
            {
                if (!await _operation.WaitAsync(0, ct)) continue;
                try
                {
                    if (_phase != Phase.EnService) continue;
                    _enfants.Surveiller();

                    if (DateTime.UtcNow >= prochaineSante)
                    {
                        prochaineSante = DateTime.UtcNow.AddSeconds(_config.IntervalleSanteSecondes);
                        await CycleSanteAsync(ct);
                    }
                }
                finally { _operation.Release(); }
            }
            else if (_phase == Phase.EchecRetentable && DateTime.UtcNow >= _prochaineTentativeUtc)
            {
                await _operation.WaitAsync(ct);
                try
                {
                    if (_phase == Phase.EchecRetentable) await DemarrerInterneAsync(ct);
                }
                finally { _operation.Release(); }
            }
        }
    }

    private async Task CycleSanteAsync(CancellationToken ct)
    {
        var ports = _etat.Ports;
        if (ports == null) return;

        var evaluation = await _sante.ExecuterCycleAsync(ports.HttpPort, _config, ct);
        _etat.AppliquerEvaluation(evaluation);
        _etat.SessionsEnCours = _base.CompterSessionsEnCours();
    }

    private async Task DemarrerInterneAsync(CancellationToken ct)
    {
        _phase = Phase.Demarrage;
        _etat.ReinitialiserPourDemarrage();
        _sante.Reinitialiser();
        _etat.Fixer(EtatServeur.Demarrage, "Démarrage en cours…");

        NettoyerProcessusOrphelins();

        Issue issue;
        try
        {
            issue = await SequenceAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Journal.Ecrire($"Erreur démarrage : {ex}");
            _etat.Fixer(EtatServeur.EnPanne, ex.Message);
            issue = Issue.Retentable;
        }

        switch (issue)
        {
            case Issue.Ok:
                _phase = Phase.EnService;
                _tentatives = 0;
                break;
            default:
                _enfants.ArreterTous();
                _tentatives++;
                _phase = Phase.EchecRetentable;
                _prochaineTentativeUtc = DateTime.UtcNow + DelaiRetentative;
                break;
        }
    }

    private async Task<Issue> SequenceAsync(CancellationToken ct)
    {
        // 1. Horloge (démarrage immédiat sans latence réseau)
        await _horloge.AttendreAsync(5, ct);

        // 2. Contrôle intégrité base
        var resultatBase = _base.Controler();
        if (!resultatBase.Ok)
        {
            _etat.BaseOk = false;
            _etat.Fixer(EtatServeur.BaseARestaurer, "La base de données est endommagée. Restaurez une sauvegarde.");
            return Issue.Bloque;
        }

        // 3. Mise à jour de version éventuelle
        var erreurMaj = await MettreAJourAsync(ct);
        if (erreurMaj != null)
        {
            _etat.Fixer(EtatServeur.EnPanne, erreurMaj);
            return Issue.Bloque;
        }

        // 4. Détection de coupure non bloquante
        var flag = FlagArretPropre.Lire();
        try { await _runner.ArtisanAsync(TimeSpan.FromSeconds(5), ct, "serveur:arret-propre", flag == true ? "1" : "0"); } catch { }

        try
        {
            var rDemarrage = await _runner.ArtisanAsync(TimeSpan.FromSeconds(15), ct, "serveur:demarrage", "--json");
            if (rDemarrage.Ok)
            {
                var bilan = JsonUtil.LireDernierObjet<DemarrageBilan>(rDemarrage.Sortie);
                _etat.Demarrage = bilan;
            }
        }
        catch (Exception ex)
        {
            Journal.Ecrire("Avertissement serveur:demarrage : " + ex.Message);
        }

        FlagArretPropre.Ecrire(false);

        // 5. Préparation Caddy et PHP (nettoyage préalable des ports)
        NettoyerProcessusOrphelins();
        var preparation = await _runner.ArtisanAsync(TimeSpan.FromSeconds(30), ct, "serveur:preparer", "--json");
        var bilanPrep = JsonUtil.LireDernierObjet<PreparationBilan>(preparation.Sortie);

        var ports = bilanPrep?.Etat ?? LecteurPorts.Lire(Chemins.PortsJson);
        if (ports == null || !LecteurPorts.Valider(ports, out var raison))
        {
            _etat.Fixer(EtatServeur.EnPanne, "Configuration des ports impossible.");
            return Issue.Retentable;
        }

        _etat.Ports = ports;

        // 6. Lancement des processus sans fenêtre
        _enfants.Configurer(ports);
        _enfants.DemarrerTous();

        // 7. Contrôle de santé
        var sante = await _sante.AttendreAsync(ports.HttpPort, TimeSpan.FromSeconds(30), _enfants.Surveiller, ct);
        _etat.ServeurDemarreUtc = DateTime.UtcNow;

        if (sante.Ok)
        {
            await CycleSanteAsync(ct);
            _etat.SessionsEnCours = _base.CompterSessionsEnCours();
        }

        return Issue.Ok;
    }

    private async Task<string?> MettreAJourAsync(CancellationToken ct)
    {
        var cible = Chemins.LireVersionApplication() ?? Constantes.VersionHote;
        string? installee = null;
        try { if (File.Exists(Chemins.VersionInstallee)) installee = File.ReadAllText(Chemins.VersionInstallee).Trim(); } catch { }

        if (installee == cible) return null;

        try
        {
            foreach (var f in Directory.GetFiles(Chemins.Cache, "*.php")) File.Delete(f);
        }
        catch { }

        await _runner.ArtisanAsync(TimeSpan.FromMinutes(2), ct, "migrate", "--force");
        try
        {
            Directory.CreateDirectory(Chemins.Etat);
            File.WriteAllText(Chemins.VersionInstallee, cible);
        }
        catch { }

        return null;
    }

    private async Task ArreterInterneAsync(bool propre)
    {
        _phase = Phase.Arrete;
        _enfants.ArreterTous();
        FlagArretPropre.Ecrire(propre);
        NettoyerProcessusOrphelins();
    }

    public async Task<ResultatAction> DemarrerAsync()
    {
        if (_phase == Phase.EnService) return new ResultatAction(true, "DEJA_EN_COURS", "Le serveur est déjà en marche.");
        if (!await _operation.WaitAsync(TimeSpan.FromSeconds(5))) return new ResultatAction(false, CodesErreur.OperationEnCours, "Opération en cours.");

        try
        {
            await DemarrerInterneAsync(_arret);
            return _phase == Phase.EnService ? new ResultatAction(true, "OK", "Serveur démarré.") : new ResultatAction(false, CodesErreur.Echec, _etat.Message);
        }
        finally { _operation.Release(); }
    }

    public async Task<ResultatAction> ArreterAsync()
    {
        if (!await _operation.WaitAsync(TimeSpan.FromSeconds(5))) return new ResultatAction(false, CodesErreur.OperationEnCours, "Opération en cours.");
        try
        {
            await ArreterInterneAsync(true);
            _etat.Fixer(EtatServeur.Arrete, "Serveur arrêté.");
            return new ResultatAction(true, "OK", "Serveur arrêté.");
        }
        finally { _operation.Release(); }
    }

    public async Task<ResultatAction> RedemarrerAsync()
    {
        if (!await _operation.WaitAsync(TimeSpan.FromSeconds(5))) return new ResultatAction(false, CodesErreur.OperationEnCours, "Opération en cours.");
        try
        {
            await ArreterInterneAsync(true);
            await DemarrerInterneAsync(_arret);
            return _phase == Phase.EnService ? new ResultatAction(true, "OK", "Serveur redémarré.") : new ResultatAction(false, CodesErreur.Echec, _etat.Message);
        }
        finally { _operation.Release(); }
    }

    public async Task<ResultatAction> SauvegarderAsync()
    {
        if (!await _sauvegarde.WaitAsync(0)) return new ResultatAction(false, CodesErreur.OperationEnCours, "Sauvegarde en cours.");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var r = await _runner.ArtisanAsync(TimeSpan.FromMinutes(3), cts.Token, "serveur:sauvegarder");
            return r.Ok ? new ResultatAction(true, "OK", "Sauvegarde réussie.") : new ResultatAction(false, CodesErreur.Echec, r.Extrait());
        }
        finally { _sauvegarde.Release(); }
    }

    public async Task<ResultatAction> CodeInstallationAsync()
    {
        if (!string.IsNullOrWhiteSpace(_config.CodeInstallation))
            return new ResultatAction(true, "OK", "Code trouvé", _config.CodeInstallation);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var r = await _runner.ArtisanAsync(TimeSpan.FromSeconds(5), cts.Token, "cyber:code-installation");
            if (r.Ok && !string.IsNullOrWhiteSpace(r.Sortie))
                return new ResultatAction(true, "OK", "Code trouvé", r.Sortie.Trim());
        }
        catch { }

        return new ResultatAction(false, CodesErreur.CodeIntrouvable, "Aucun code configuré.");
    }
}