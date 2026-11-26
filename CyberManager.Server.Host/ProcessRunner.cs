using System.Diagnostics;
using System.Text;
using CyberManager.Server.Common;

namespace CyberManager.Server.Host;

internal sealed record ResultatProcessus(int Code, string Sortie, string Erreur, bool DelaiDepasse)
{
    public bool Ok => Code == 0 && !DelaiDepasse;

    public string Extrait(int max = 400)
    {
        if (DelaiDepasse) return "délai dépassé";

        var texte = (Erreur.Trim().Length > 0 ? Erreur : Sortie).Trim();
        return texte.Length <= max ? texte : texte[..max] + "…";
    }
}

/// <summary>Lance des commandes courtes (artisan, w32tm...) sans fenêtre, avec délai maximal. Les processus rejoignent le Job Object.</summary>
internal sealed class ProcessRunner
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly JobObject _job;

    public ProcessRunner(JobObject job)
    {
        _job = job;
    }

    public async Task<ResultatProcessus> ExecuterAsync(
        string exe,
        IEnumerable<string> arguments,
        TimeSpan delai,
        CancellationToken ct,
        string? dossier = null,
        bool environnementPhp = true)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = dossier ?? Chemins.App,
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

        if (environnementPhp)
        {
            EnvironnementPhp.Appliquer(psi);
        }

        using var processus = new Process { StartInfo = psi };

        try
        {
            processus.Start();
        }
        catch (Exception ex)
        {
            return new ResultatProcessus(-1, "", $"Lancement impossible ({exe}) : {ex.Message}", false);
        }

        try
        {
            _job.Assigner(processus);
        }
        catch (Exception ex)
        {
            Journal.Ecrire($"ATTENTION : processus {Path.GetFileName(exe)} non rattaché au Job Object : {ex.Message}");
        }

        var lectureSortie = processus.StandardOutput.ReadToEndAsync();
        var lectureErreur = processus.StandardError.ReadToEndAsync();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(delai);

        var delaiDepasse = false;
        try
        {
            await processus.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            delaiDepasse = !ct.IsCancellationRequested;
            try { processus.Kill(true); } catch { }

            if (ct.IsCancellationRequested)
            {
                throw;
            }
        }

        // Laisse un court instant pour vider les flux, sans jamais rester bloqué
        await Task.WhenAny(Task.WhenAll(lectureSortie, lectureErreur), Task.Delay(3000, CancellationToken.None));

        var sortie = lectureSortie.IsCompletedSuccessfully ? lectureSortie.Result : "";
        var erreur = lectureErreur.IsCompletedSuccessfully ? lectureErreur.Result : "";

        int code;
        try { code = delaiDepasse ? -2 : processus.ExitCode; }
        catch { code = -1; }

        return new ResultatProcessus(code, sortie, erreur, delaiDepasse);
    }

    /// <summary>php.exe artisan &lt;arguments&gt; (variables d'environnement §2.2.3 appliquées).</summary>
    public Task<ResultatProcessus> ArtisanAsync(TimeSpan delai, CancellationToken ct, params string[] arguments)
    {
        var liste = new List<string> { "artisan" };
        liste.AddRange(arguments);

        return ExecuterAsync(Chemins.PhpCli, liste, delai, ct);
    }
}