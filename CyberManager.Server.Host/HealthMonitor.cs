using System.Text.Json;
using CyberManager.Server.Common;
using CyberManager.Server.Common.Ipc;

namespace CyberManager.Server.Host;

internal sealed record ResultatSante(
    bool Joignable,
    bool Ok,
    bool BaseOk,
    double? PlanificateurSecondes,
    bool HorlogeBloquee,
    string? Erreur);

internal sealed record EvaluationSante(
    string Etat,
    string Message,
    List<string> Avertissements,
    ResultatSante Sante);

/// <summary>
/// Interroge GET /api/sante avec une tolérance robuste aux ralentissements passagers.
/// Ne coupe jamais Caddy brutalement pour un simple pic de charge.
/// </summary>
internal sealed class HealthMonitor
{
    // Tolérance de 10 s au lieu de 5 s pour absorber les accès disque sans paniquer
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(120);

    private readonly ChildProcessManager _enfants;
    private readonly DatabaseGuard _base;

    private int _echecsWeb;
    private int _echecsBase;
    private bool _baseCorrompue;
    private DateTime _dernierRedemarrageWeb = DateTime.MinValue;
    private DateTime _dernierRedemarragePlanificateur = DateTime.MinValue;

    public HealthMonitor(ChildProcessManager enfants, DatabaseGuard baseGuard)
    {
        _enfants = enfants;
        _base = baseGuard;
    }

    public void Reinitialiser()
    {
        _echecsWeb = 0;
        _echecsBase = 0;
        _baseCorrompue = false;
        _dernierRedemarrageWeb = DateTime.MinValue;
        _dernierRedemarragePlanificateur = DateTime.MinValue;
    }

    public async Task<ResultatSante> InterrogerAsync(int port, CancellationToken ct)
    {
        try
        {
            using var reponse = await Http.GetAsync($"http://127.0.0.1:{port}/api/sante", ct);
            var corps = await reponse.Content.ReadAsStringAsync(ct);

            bool ok = false, baseOk = false, horloge = false;
            double? planificateur = null;

            try
            {
                using var doc = JsonDocument.Parse(corps);
                var r = doc.RootElement;
                if (r.ValueKind == JsonValueKind.Object)
                {
                    ok = Booleen(r, "ok");
                    baseOk = Booleen(r, "base");
                    horloge = Booleen(r, "horloge_bloquee");

                    if (r.TryGetProperty("planificateur_s", out var p) && p.ValueKind == JsonValueKind.Number)
                    {
                        planificateur = p.GetDouble();
                    }
                }
            }
            catch (JsonException)
            {
            }

            return new ResultatSante(
                true,
                reponse.IsSuccessStatusCode && ok,
                baseOk,
                planificateur,
                horloge,
                reponse.IsSuccessStatusCode ? null : $"HTTP {(int)reponse.StatusCode}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ResultatSante(false, false, false, null, false, ex.Message);
        }
    }

    private static bool Booleen(JsonElement racine, string nom) =>
        racine.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.True;

    public async Task<ResultatSante> AttendreAsync(int port, TimeSpan delai, Action surveiller, CancellationToken ct)
    {
        var limite = DateTime.UtcNow + delai;
        ResultatSante dernier;

        while (true)
        {
            surveiller();
            dernier = await InterrogerAsync(port, ct);

            if (dernier.Ok || DateTime.UtcNow >= limite)
            {
                return dernier;
            }

            await Task.Delay(1000, ct);
        }
    }

    public async Task<EvaluationSante> ExecuterCycleAsync(int port, ServeurConfig config, CancellationToken ct)
    {
        var avertissements = new List<string>();
        var s = await InterrogerAsync(port, ct);

        if (!s.Joignable)
        {
            _echecsWeb++;
            _echecsBase = 0;
            avertissements.Add("Le serveur web répond lentement.");

            // Seuil porté à 6 échecs consécutifs (60 secondes de panne réelle) avant d'envisager une relance
            if (_echecsWeb >= 6 && PeutAgir(ref _dernierRedemarrageWeb))
            {
                _enfants.Redemarrer(TypeProcessus.Caddy, $"aucune réponse de santé depuis {_echecsWeb} contrôles");
            }
        }
        else
        {
            _echecsWeb = 0;

            if (s.Ok)
            {
                _echecsBase = 0;
                _baseCorrompue = false;
            }
            else if (!s.BaseOk)
            {
                _echecsBase++;
                avertissements.Add("La base de données ne répond pas correctement.");

                if (_echecsBase >= 4)
                {
                    var controle = _base.Controler();
                    _baseCorrompue = !controle.Ok;
                    if (_baseCorrompue)
                    {
                        Journal.Ecrire($"Santé : contrôle intégrité échoué : {controle.Detail}");
                    }
                }
            }
        }

        // Retard du planificateur : avertissement bienveillant sans tuer les processus
        if (s.Joignable && s.Ok && s.PlanificateurSecondes is double retard && !s.HorlogeBloquee
            && retard > Math.Max(90, config.RetardPlanificateurSecondes))
        {
            avertissements.Add($"Tâches de fond en cours ({retard:F0} s).");
        }

        string etat;
        string message;

        var panne = _enfants.PanneCritique();
        if (panne != null)
        {
            etat = EtatServeur.EnPanne;
            message = panne;
        }
        else if (!s.Joignable && _echecsWeb >= 6)
        {
            etat = EtatServeur.EnPanne;
            message = "Le serveur web ne répond plus.";
        }
        else if (_baseCorrompue)
        {
            etat = EtatServeur.BaseARestaurer;
            message = "La base de données semble endommagée : restauration nécessaire.";
        }
        else if (s.HorlogeBloquee)
        {
            etat = EtatServeur.HorlogeAVerifier;
            message = "L'heure de l'ordinateur doit être vérifiée.";
        }
        else
        {
            avertissements.AddRange(_enfants.Degradations());

            if (avertissements.Count > 0)
            {
                etat = EtatServeur.Degrade;
                message = avertissements[0];
            }
            else
            {
                etat = EtatServeur.Pret;
                message = "Tout fonctionne.";
            }
        }

        return new EvaluationSante(etat, message, avertissements, s);
    }

    private static bool PeutAgir(ref DateTime dernier)
    {
        if (DateTime.UtcNow - dernier < Cooldown) return false;
        dernier = DateTime.UtcNow;
        return true;
    }
}