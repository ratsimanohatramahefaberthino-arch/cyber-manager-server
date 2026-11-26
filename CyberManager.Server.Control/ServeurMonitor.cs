using System.ComponentModel;
using System.ServiceProcess;
using CyberManager.Server.Common;
using CyberManager.Server.Common.Ipc;

namespace CyberManager.Server.Control;

internal enum Liaison
{
    /// <summary>Premier contact en cours.</summary>
    Connexion,
    /// <summary>Le service répond.</summary>
    Connecte,
    /// <summary>Le service ne répond plus depuis un instant : nouvelle tentative automatique.</summary>
    Reconnexion,
    ServiceArrete,
    ServiceAbsent,
    /// <summary>Service Windows lancé mais pipe muet.</summary>
    Injoignable
}

internal sealed class InstantaneServeur
{
    public Liaison Liaison { get; init; }

    /// <summary>Dernier statut connu (conservé pendant une reconnexion transitoire pour éviter le clignotement).</summary>
    public StatutServeur? Statut { get; init; }

    public DateTime? DernierSuccesLocal { get; init; }
    public string Detail { get; init; } = "";
}

internal enum EtatServiceWindows { Inconnu, Absent, Arrete, Demarrage, Marche }

/// <summary>Interroge le service toutes les 2 s (GetStatus) et publie un instantané sur le thread de l'interface.</summary>
internal sealed class ServeurMonitor : IDisposable
{
    private const int EchecsAvantInjoignable = 2;

    private readonly System.Windows.Forms.Timer _minuteur = new() { Interval = 2000 };
    private bool _enCours;
    private int _echecs;
    private StatutServeur? _dernierStatut;
    private DateTime? _dernierSucces;

    public InstantaneServeur Courant { get; private set; } = new()
    {
        Liaison = Liaison.Connexion,
        Detail = "Connexion au service…"
    };

    public event Action<InstantaneServeur>? Changement;

    public void Demarrer()
    {
        _minuteur.Tick += async (_, _) => await ActualiserAsync();
        _minuteur.Start();
        _ = ActualiserAsync();
    }

    /// <summary>Demande une actualisation immédiate (sans attendre le prochain tic).</summary>
    public void Actualiser() => _ = ActualiserAsync();

    public async Task ActualiserAsync()
    {
        if (_enCours) return;
        _enCours = true;

        try
        {
            await InterrogerAsync();
        }
        catch (Exception ex)
        {
            JournalControle.Ecrire("Interrogation du service : " + ex);
        }
        finally
        {
            _enCours = false;
        }
    }

    private async Task InterrogerAsync()
    {
        var r = await PipeClient.EnvoyerAsync(Commandes.GetStatus, false, TimeSpan.FromSeconds(6));

        if (r.Ok && r.Reponse?.Statut is { } statut)
        {
            _echecs = 0;
            _dernierStatut = statut;
            _dernierSucces = DateTime.Now;
            Publier(Liaison.Connecte, "");
            return;
        }

        _echecs++;

        switch (await Task.Run(LireEtatService))
        {
            case EtatServiceWindows.Absent:
                _dernierStatut = null;
                Publier(Liaison.ServiceAbsent, "Le service Windows « " + Constantes.NomAffiche + " » n'est pas installé.");
                return;

            case EtatServiceWindows.Arrete:
                _dernierStatut = null;
                Publier(Liaison.ServiceArrete, "Le service Windows est arrêté.");
                return;

            case EtatServiceWindows.Demarrage:
                Publier(Liaison.Reconnexion, "Le service Windows est en cours de démarrage…");
                return;
        }

        if (_echecs < EchecsAvantInjoignable)
        {
            Publier(_dernierStatut != null ? Liaison.Reconnexion : Liaison.Connexion, "");
        }
        else
        {
            Publier(Liaison.Injoignable, r.Erreur ?? r.Message);
        }
    }

    private void Publier(Liaison liaison, string detail)
    {
        var garderStatut = liaison is Liaison.Connecte or Liaison.Reconnexion;

        Courant = new InstantaneServeur
        {
            Liaison = liaison,
            Statut = garderStatut ? _dernierStatut : null,
            DernierSuccesLocal = _dernierSucces,
            Detail = detail
        };

        Changement?.Invoke(Courant);
    }

    private static EtatServiceWindows LireEtatService()
    {
        try
        {
            using var sc = new ServiceController(Constantes.NomService);

            return sc.Status switch
            {
                ServiceControllerStatus.Running => EtatServiceWindows.Marche,
                ServiceControllerStatus.StartPending or ServiceControllerStatus.ContinuePending => EtatServiceWindows.Demarrage,
                ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending => EtatServiceWindows.Arrete,
                _ => EtatServiceWindows.Inconnu
            };
        }
        catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: 1060 })
        {
            return EtatServiceWindows.Absent;   // ERROR_SERVICE_DOES_NOT_EXIST
        }
        catch
        {
            return EtatServiceWindows.Inconnu;
        }
    }

    public void Dispose() => _minuteur.Dispose();
}