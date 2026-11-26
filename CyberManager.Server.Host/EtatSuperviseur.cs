using CyberManager.Server.Common;
using CyberManager.Server.Common.Ipc;

namespace CyberManager.Server.Host;

/// <summary>État partagé du service (écrit par le superviseur, lu par le pipe) et fabrique du StatutServeur.</summary>
internal sealed class EtatSuperviseur
{
    private readonly object _verrou = new();
    private string _etat = EtatServeur.Demarrage;
    private string _message = "Démarrage du service…";

    private List<string> _avertissementsDemarrage = new();
    private List<string> _avertissementsDynamiques = new();

    private DateTime _derniereLectureSauvegarde = DateTime.MinValue;
    private DateTimeOffset? _derniereSauvegarde;

    public DateTime ServiceDemarreUtc { get; } = DateTime.UtcNow;
    public PortsJson? Ports { get; set; }
    public DemarrageBilan? Demarrage { get; set; }
    public DateTime? ServeurDemarreUtc { get; set; }
    public bool BaseOk { get; set; } = true;
    public int? SessionsEnCours { get; set; }
    public double? PlanificateurSecondes { get; set; }
    public bool HorlogeBloquee { get; set; }

    public string Etat
    {
        get { lock (_verrou) return _etat; }
    }

    public string Message
    {
        get { lock (_verrou) return _message; }
    }

    public void Fixer(string etat, string message)
    {
        lock (_verrou)
        {
            if (_etat != etat || _message != message)
            {
                Journal.Ecrire($"État : {etat} — {message}");
            }

            _etat = etat;
            _message = message;
        }
    }

    public void ReinitialiserPourDemarrage()
    {
        lock (_verrou)
        {
            _avertissementsDemarrage = new List<string>();
            _avertissementsDynamiques = new List<string>();
        }

        Demarrage = null;
        ServeurDemarreUtc = null;
        PlanificateurSecondes = null;
        HorlogeBloquee = false;
        BaseOk = true;
    }

    public void AjouterAvertissementDemarrage(string texte)
    {
        lock (_verrou)
        {
            _avertissementsDemarrage.Add(texte);
        }

        Journal.Ecrire("Avertissement : " + texte);
    }

    public void AppliquerEvaluation(EvaluationSante e)
    {
        Fixer(e.Etat, e.Message);

        lock (_verrou)
        {
            _avertissementsDynamiques = e.Avertissements;
        }

        BaseOk = !e.Sante.Joignable || e.Sante.BaseOk;
        PlanificateurSecondes = e.Sante.PlanificateurSecondes;
        HorlogeBloquee = e.Sante.HorlogeBloquee;
    }

    public StatutServeur Instantane(ChildProcessManager enfants)
    {
        string etat, message;
        List<string> avertissements;

        lock (_verrou)
        {
            etat = _etat;
            message = _message;
            avertissements = _avertissementsDemarrage.Concat(_avertissementsDynamiques).Distinct().ToList();
        }

        var processus = enfants.Instantane();
        var workers = processus.Where(p => p.Type == TypeProcessus.Worker).ToList();
        var ports = Ports;
        var demarre = ServeurDemarreUtc;

        var statut = new StatutServeur
        {
            Etat = etat,
            Message = message,
            VersionHote = Constantes.VersionHote,
            VersionApplication = Chemins.LireVersionApplication(),
            ServiceDemarreLe = new DateTimeOffset(ServiceDemarreUtc),
            ServeurDemarreLe = demarre.HasValue ? new DateTimeOffset(demarre.Value) : null,
            DureeFonctionnementSecondes = demarre.HasValue ? (long)(DateTime.UtcNow - demarre.Value).TotalSeconds : 0,
            PortHttp = ports?.HttpPort,
            PortUdpDecouverte = ports != null && ports.Decouverte.UdpPort > 0 ? ports.Decouverte.UdpPort : null,
            Urls = ports?.Urls.ToList() ?? new List<string>(),
            Caddy = processus.FirstOrDefault(p => p.Type == TypeProcessus.Caddy),
            Workers = workers,
            WorkersTotal = workers.Count,
            WorkersActifs = workers.Count(w => w.Vivant),
            Planificateur = processus.FirstOrDefault(p => p.Type == TypeProcessus.Planificateur),
            Decouverte = processus.FirstOrDefault(p => p.Type == TypeProcessus.Decouverte),
            PlanificateurDernierPassageSecondes = PlanificateurSecondes,
            BaseOk = BaseOk,
            SessionsEnCours = SessionsEnCours,
            HorlogeBloquee = HorlogeBloquee,
            Avertissements = avertissements,
            DerniereSauvegarde = LireDerniereSauvegarde(),
            EspaceDisqueLibreMo = LireEspaceDisque()
        };

        var d = Demarrage;
        if (d != null && d.Type is "coupure_serveur" or "horloge_suspecte")
        {
            statut.Incident = new IncidentCoupure
            {
                Id = d.IncidentId,
                Type = d.Type,
                SessionsFigees = d.SessionsFigees,
                MontantFige = d.MontantFige,
                EcartSecondes = d.EcartSecondes
            };
        }

        return statut;
    }

    private DateTimeOffset? LireDerniereSauvegarde()
    {
        if (DateTime.UtcNow - _derniereLectureSauvegarde < TimeSpan.FromSeconds(30))
        {
            return _derniereSauvegarde;
        }

        _derniereLectureSauvegarde = DateTime.UtcNow;

        try
        {
            if (Directory.Exists(Chemins.Sauvegardes))
            {
                var plusRecent = new DirectoryInfo(Chemins.Sauvegardes)
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Select(f => f.LastWriteTimeUtc)
                    .DefaultIfEmpty()
                    .Max();

                _derniereSauvegarde = plusRecent == default ? null : new DateTimeOffset(DateTime.SpecifyKind(plusRecent, DateTimeKind.Utc));
            }
            else
            {
                _derniereSauvegarde = null;
            }
        }
        catch
        {
            _derniereSauvegarde = null;
        }

        return _derniereSauvegarde;
    }

    private static long? LireEspaceDisque()
    {
        try
        {
            var racine = Path.GetPathRoot(Chemins.Donnees);
            return string.IsNullOrEmpty(racine) ? null : new DriveInfo(racine).AvailableFreeSpace / (1024 * 1024);
        }
        catch
        {
            return null;
        }
    }
}