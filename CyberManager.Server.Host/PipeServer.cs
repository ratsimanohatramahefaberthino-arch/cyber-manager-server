using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using CyberManager.Server.Common;
using CyberManager.Server.Common.Ipc;

namespace CyberManager.Server.Host;

internal sealed record ResultatAction(bool Ok, string Code, string Message, string? Details = null);

/// <summary>Actions que le pipe peut demander au superviseur.</summary>
internal interface IControleServeur
{
    Task<ResultatAction> DemarrerAsync();
    Task<ResultatAction> ArreterAsync();
    Task<ResultatAction> RedemarrerAsync();
    Task<ResultatAction> SauvegarderAsync();
    Task<ResultatAction> CodeInstallationAsync();
}

/// <summary>
/// \\.\pipe\CyberManagerServer : JSON v1, une ligne par message.
///   - Lecture du statut : tout utilisateur local authentifié.
///   - Stop / Restart : administrateur Windows, OU confirmation explicite (« confirmer »: true) donnée dans la télécommande ;
///     avec des sessions en cours, la confirmation est toujours exigée.
///   - Accès réseau refusé (ACL + rejet des clients distants).
///   - Aucun secret ne transite ni n'est journalisé.
/// </summary>
internal sealed class PipeServer
{
    private const int ConnexionsMax = 16;
    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly EtatSuperviseur _etat;
    private readonly ChildProcessManager _enfants;
    private readonly DatabaseGuard _base;

    private CancellationTokenSource? _cts;
    private Task? _boucle;
    private int _connexions;

    public IControleServeur? Controle { get; set; }

    public PipeServer(EtatSuperviseur etat, ChildProcessManager enfants, DatabaseGuard baseGuard)
    {
        _etat = etat;
        _enfants = enfants;
        _base = baseGuard;
    }

    public void Demarrer(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var jeton = _cts.Token;
        _boucle = Task.Run(() => BoucleAsync(jeton));
        Journal.Ecrire($"Pipe de contrôle : \\\\.\\pipe\\{Constantes.NomPipe}");
    }

    public async Task ArreterAsync()
    {
        try { _cts?.Cancel(); } catch { }

        if (_boucle != null)
        {
            await Task.WhenAny(_boucle, Task.Delay(3000));
        }
    }

    // ------------------------------------------------------------------
    // Boucle d'acceptation
    // ------------------------------------------------------------------

    private async Task BoucleAsync(CancellationToken ct)
    {
        var premiere = true;

        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;

            try
            {
                pipe = Creer(premiere);
                premiere = false;

                await pipe.WaitForConnectionAsync(ct);

                if (Interlocked.Increment(ref _connexions) > ConnexionsMax)
                {
                    Interlocked.Decrement(ref _connexions);
                    continue;   // le finally ferme le pipe
                }

                var transmis = pipe;
                pipe = null;

                _ = Task.Run(async () =>
                {
                    try { await TraiterConnexionAsync(transmis, ct); }
                    finally { Interlocked.Decrement(ref _connexions); }
                });
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Journal.Ecrire($"Pipe : erreur d'écoute : {ex.Message}");

                try { await Task.Delay(2000, ct); }
                catch (OperationCanceledException) { break; }
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    private static NamedPipeServerStream Creer(bool premiere)
    {
        var securite = new PipeSecurity();

        // Le compte du service doit pouvoir créer les instances suivantes du pipe
        using (var moi = WindowsIdentity.GetCurrent())
        {
            if (moi.User != null)
            {
                securite.AddAccessRule(new PipeAccessRule(moi.User, PipeAccessRights.FullControl, AccessControlType.Allow));
            }
        }

        securite.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        securite.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        securite.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        securite.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));

        // La toute première instance exige que le nom ne soit pas déjà pris (anti-usurpation du pipe)
        var options = PipeOptions.Asynchronous | (premiere ? PipeOptions.FirstPipeInstance : PipeOptions.None);

        return NamedPipeServerStreamAcl.Create(
            Constantes.NomPipe,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            options,
            4096,
            4096,
            securite);
    }

    // ------------------------------------------------------------------
    // Une connexion = plusieurs échanges « une ligne → une ligne »
    // ------------------------------------------------------------------

    private async Task TraiterConnexionAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            using (pipe)
            using (var lecteur = new StreamReader(pipe, Utf8, false, 4096, leaveOpen: true))
            using (var ecrivain = new StreamWriter(pipe, Utf8, 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" })
            {
                while (pipe.IsConnected && !ct.IsCancellationRequested)
                {
                    string? ligne;

                    using (var delai = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        delai.CancelAfter(TimeSpan.FromSeconds(30));

                        try { ligne = await lecteur.ReadLineAsync(delai.Token); }
                        catch (OperationCanceledException) { break; }
                    }

                    if (ligne == null) break;

                    PipeReponse reponse;
                    if (ligne.Length > 65536)
                    {
                        reponse = Echec(null, CodesErreur.RequeteInvalide, "Message trop long.");
                    }
                    else
                    {
                        try
                        {
                            reponse = await TraiterAsync(pipe, ligne);
                        }
                        catch (Exception ex)
                        {
                            Journal.Ecrire($"Pipe : erreur de traitement : {ex.Message}");
                            reponse = Echec(null, CodesErreur.ErreurInterne, "Erreur interne du service.");
                        }
                    }

                    await ecrivain.WriteLineAsync(ProtocolePipe.EncoderReponse(reponse));
                }
            }
        }
        catch (Exception ex)
        {
            // client parti brutalement : sans importance
            Journal.Ecrire($"Pipe : connexion terminée ({ex.GetType().Name}).");
        }
    }

    // ------------------------------------------------------------------
    // Traitement d'une commande
    // ------------------------------------------------------------------

    private async Task<PipeReponse> TraiterAsync(NamedPipeServerStream pipe, string ligne)
    {
        var requete = ProtocolePipe.DecoderRequete(ligne);
        if (requete == null)
        {
            return Echec(null, CodesErreur.RequeteInvalide, "Message illisible.");
        }

        if (requete.V != Constantes.VersionProtocole)
        {
            return Echec(requete, CodesErreur.VersionNonSupportee,
                $"Protocole version {requete.V} non supporté (attendu : {Constantes.VersionProtocole}).");
        }

        var cmd = requete.Cmd ?? "";

        if (Egal(cmd, Commandes.GetStatus))
        {
            return new PipeReponse
            {
                Ok = true,
                Cmd = cmd,
                Id = requete.Id,
                Statut = _etat.Instantane(_enfants)
            };
        }

        var controle = Controle;
        if (controle == null)
        {
            return Echec(requete, CodesErreur.ServiceOccupe, "Le service démarre, réessayez dans un instant.");
        }

        if (Egal(cmd, Commandes.Start)) return Convertir(requete, await controle.DemarrerAsync());
        if (Egal(cmd, Commandes.BackupNow)) return Convertir(requete, await controle.SauvegarderAsync());

        if (Egal(cmd, Commandes.GetInstallCode))
        {
            var r = await controle.CodeInstallationAsync();
            var reponse = Convertir(requete, r);
            if (r.Ok) reponse.CodeInstallation = r.Details;
            reponse.Details = null;   // le code n'est jamais répété dans un autre champ
            return reponse;
        }

        if (Egal(cmd, Commandes.Stop) || Egal(cmd, Commandes.Restart))
        {
            var refus = VerifierAutorisation(pipe, requete);
            if (refus != null) return refus;

            return Convertir(requete, Egal(cmd, Commandes.Stop)
                ? await controle.ArreterAsync()
                : await controle.RedemarrerAsync());
        }

        return Echec(requete, CodesErreur.CommandeInconnue, $"Commande inconnue : {cmd}");
    }

    /// <summary>Stop/Restart : confirmation si des sessions sont en cours, ou si l'appelant n'est pas administrateur.</summary>
    private PipeReponse? VerifierAutorisation(NamedPipeServerStream pipe, PipeRequete requete)
    {
        if (requete.Confirmer && !requete.Cmd!.Equals("", StringComparison.Ordinal))
        {
            return null;
        }

        var sessions = _base.CompterSessionsEnCours() ?? 0;
        if (sessions > 0)
        {
            var r = Echec(requete, CodesErreur.ConfirmationRequise,
                $"{sessions} session(s) payante(s) sont en cours : elles seront figées. Confirmez pour continuer.");
            r.SessionsEnCours = sessions;
            return r;
        }

        if (!EstAdministrateur(pipe))
        {
            return Echec(requete, CodesErreur.ConfirmationRequise,
                "Cette action doit être confirmée dans la télécommande.");
        }

        return null;
    }

    private static bool EstAdministrateur(NamedPipeServerStream pipe)
    {
        var admin = false;

        try
        {
            pipe.RunAsClient(() =>
            {
                using var identite = WindowsIdentity.GetCurrent();
                admin = new WindowsPrincipal(identite).IsInRole(WindowsBuiltInRole.Administrator);
            });
        }
        catch
        {
            admin = false;
        }

        return admin;
    }

    private static bool Egal(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);

    private static PipeReponse Convertir(PipeRequete requete, ResultatAction r) => new()
    {
        Ok = r.Ok,
        Cmd = requete.Cmd,
        Id = requete.Id,
        Code = r.Ok ? null : r.Code,
        Message = r.Message,
        Details = r.Details
    };

    private static PipeReponse Echec(PipeRequete? requete, string code, string message) => new()
    {
        Ok = false,
        Cmd = requete?.Cmd,
        Id = requete?.Id,
        Code = code,
        Message = message
    };
}