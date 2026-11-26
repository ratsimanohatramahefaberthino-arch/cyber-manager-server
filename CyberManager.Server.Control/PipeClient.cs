using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using CyberManager.Server.Common;
using CyberManager.Server.Common.Ipc;

namespace CyberManager.Server.Control;

internal sealed class ResultatPipe
{
    public PipeReponse? Reponse { get; init; }
    public string? Erreur { get; init; }

    public bool Joint => Reponse != null;
    public bool Ok => Reponse is { Ok: true };
    public string Message => Reponse?.Message ?? Erreur ?? "Réponse vide du service.";
}

/// <summary>
/// Client du Named Pipe \\.\pipe\CyberManagerServer (JSON v1, une ligne par message).
/// Une connexion par requête : aucun état à réparer quand le service redémarre (reconnexion transparente).
/// </summary>
internal static class PipeClient
{
    private const int DelaiConnexionMs = 1500;
    private static readonly UTF8Encoding Utf8 = new(false);

    public static async Task<ResultatPipe> EnvoyerAsync(string commande, bool confirmer, TimeSpan delai, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N")[..8];

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(delai);

        try
        {
            // « Identification » : le service peut vérifier si l'appelant est administrateur (RunAsClient)
            using var pipe = new NamedPipeClientStream(
                ".", Constantes.NomPipe, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);

            await pipe.ConnectAsync(DelaiConnexionMs, cts.Token).ConfigureAwait(false);

            var requete = new PipeRequete { Cmd = commande, Confirmer = confirmer, Id = id };
            var octets = Utf8.GetBytes(ProtocolePipe.EncoderRequete(requete) + "\n");

            await pipe.WriteAsync(octets, cts.Token).ConfigureAwait(false);
            await pipe.FlushAsync(cts.Token).ConfigureAwait(false);

            using var lecteur = new StreamReader(pipe, Utf8, false, 4096, leaveOpen: true);
            var ligne = await lecteur.ReadLineAsync(cts.Token).ConfigureAwait(false);

            if (ligne == null)
            {
                return Echec("Le service a fermé la connexion.");
            }

            var reponse = ProtocolePipe.DecoderReponse(ligne);
            return reponse == null
                ? Echec("Réponse illisible du service.")
                : new ResultatPipe { Reponse = reponse };
        }
        catch (TimeoutException)
        {
            return Echec("Le canal de contrôle du service est introuvable.");
        }
        catch (OperationCanceledException)
        {
            return Echec(ct.IsCancellationRequested ? "Opération annulée." : "Le service n'a pas répondu à temps.");
        }
        catch (UnauthorizedAccessException)
        {
            return Echec("Accès refusé au canal de contrôle du service.");
        }
        catch (IOException ex)
        {
            return Echec("Communication interrompue avec le service : " + ex.Message);
        }
        catch (Exception ex)
        {
            return Echec(ex.Message);
        }
    }

    private static ResultatPipe Echec(string message) => new() { Erreur = message };
}