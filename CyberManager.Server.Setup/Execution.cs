using System.Diagnostics;
using System.Text;
using CyberManager.Server.Host;

namespace CyberManager.Server.Setup;

internal sealed record ResultatExecution(int? Code, string Sortie, string Erreur, bool DelaiDepasse)
{
    public bool Ok => Code == 0 && !DelaiDepasse;

    public string Extrait()
    {
        if (DelaiDepasse) return "délai dépassé";

        var texte = (Erreur.Trim().Length > 0 ? Erreur : Sortie).Trim();
        return texte.Length > 400 ? texte[..400] + "…" : texte;
    }
}

/// <summary>Lance un programme sans fenêtre, avec délai maximal, et option d'entrée standard (jamais d'argument secret).</summary>
internal static class Execution
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public static ResultatExecution Lancer(
        string exe,
        IEnumerable<string> arguments,
        TimeSpan delai,
        string? dossier = null,
        string? entree = null,
        bool environnementPhp = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = entree != null,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8
        };

        if (entree != null) psi.StandardInputEncoding = Utf8;
        if (dossier != null && Directory.Exists(dossier)) psi.WorkingDirectory = dossier;

        foreach (var a in arguments) psi.ArgumentList.Add(a);

        if (environnementPhp) EnvironnementPhp.Appliquer(psi);

        var sortie = new StringBuilder();
        var erreur = new StringBuilder();

        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (sortie) sortie.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (erreur) erreur.AppendLine(e.Data); };

        try
        {
            p.Start();
        }
        catch (Exception ex)
        {
            return new ResultatExecution(null, "", $"Impossible de lancer {exe} : {ex.Message}", false);
        }

        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        if (entree != null)
        {
            try
            {
                p.StandardInput.Write(entree);
                p.StandardInput.Close();
            }
            catch
            {
                // le processus s'est terminé avant d'avoir lu : son code de sortie le dira
            }
        }

        if (!p.WaitForExit((int)delai.TotalMilliseconds))
        {
            try { p.Kill(true); } catch { /* déjà terminé */ }

            return new ResultatExecution(null, sortie.ToString(), erreur.ToString(), true);
        }

        p.WaitForExit();   // vide les flux asynchrones

        return new ResultatExecution(p.ExitCode, sortie.ToString(), erreur.ToString(), false);
    }
}