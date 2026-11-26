using System.Reflection;
using CyberManager.Server.Common;

namespace CyberManager.Server.Setup;

internal static class Commandes
{
    public static readonly int[] PortsParDefaut = { 80, 8080, 8000, 8081, 8888 };

    public static int Detecter(Arguments a)
    {
        var format = Format(a);
        var cible = a.Valeur("version-cible") ?? Chemins.LireVersionApplication() ?? Constantes.VersionHote;
        if (!VersionsApp.TryParse(cible, out _))
        {
            throw new ArgumentException($"Version cible invalide : « {cible} ».");
        }

        var ports = AnalyserPorts(a.Valeur("ports")) ?? PortsParDefaut;

        var r = new RapportDetection { VersionCible = cible };
        r.Systeme = DetectionSysteme.Detecter();
        r.Installation = DetectionInstallation.Detecter(cible);
        r.Reseau = DetectionReseau.Detecter();
        r.Ports = SondePort.SonderListe(ports);
        r.Xampp = DetectionXampp.Detecter();

        var libre = r.Ports.FirstOrDefault(p => p.Etat == EtatPort.Libre);
        r.PortRecommande = libre?.Port;
        r.PortOrigine = libre == null ? "aucun" : (libre.Port == ports[0] ? "prefere" : "repli");

        Diagnostic.Evaluer(r);

        Sortie.Emettre(r, format, a.Valeur("sortie"));
        return 0;
    }

    public static int SonderPorts(Arguments a)
    {
        var format = Format(a);
        var liste = AnalyserPorts(a.Valeur("ports") ?? a.Valeur("port"))
                    ?? throw new ArgumentException("Indiquez --port=N ou --ports=N,M,...");

        var r = new RapportPorts { Ports = SondePort.SonderListe(liste) };
        r.PortRecommande = r.Ports.FirstOrDefault(p => p.Etat == EtatPort.Libre)?.Port;

        Sortie.Emettre(r, format, a.Valeur("sortie"));
        return 0;
    }

    public static int Version()
    {
        Console.WriteLine(Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?");
        return 0;
    }

    private static string Format(Arguments a)
    {
        var f = (a.Valeur("format") ?? "json").ToLowerInvariant();
        if (f != "json" && f != "ini") throw new ArgumentException("--format doit valoir json ou ini.");
        return f;
    }

    internal static int[]? AnalyserPorts(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte)) return null;

        var morceaux = texte.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var res = new List<int>();

        foreach (var m in morceaux)
        {
            if (!int.TryParse(m, out var p) || p < 1 || p > 65535)
            {
                throw new ArgumentException($"Port invalide : « {m} » (entier de 1 à 65535).");
            }

            if (!res.Contains(p)) res.Add(p);
        }

        return res.Count == 0 ? null : res.ToArray();
    }
}