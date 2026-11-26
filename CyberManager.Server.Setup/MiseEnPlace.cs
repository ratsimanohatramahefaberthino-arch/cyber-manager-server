using System.Diagnostics;
using CyberManager.Server.Common;

namespace CyberManager.Server.Setup;

internal static class CommandeMiseEnPlace
{
    public static int Executer(Arguments a)
    {
        var format = (a.Valeur("format") ?? "json").ToLowerInvariant();
        if (format != "json" && format != "ini") throw new ArgumentException("--format doit valoir json ou ini.");

        var etapes = MiseEnPlace.AnalyserEtapes(a.Valeur("etape"));

        var demande = a.Valeur("demande") is { Length: > 0 } fichier
            ? Demande.Charger(fichier, effacer: !a.Present("garder-demande"))
            : new Demande();

        var rapport = MiseEnPlace.Lancer(demande, etapes);

        Sortie.Emettre(rapport, format, a.Valeur("sortie"));
        return rapport.Ok ? 0 : 3;
    }
}

internal static class MiseEnPlace
{
    public static readonly string[] Ordre =
        { "arreter", "dossiers", "configuration", "taches", "parefeu", "base", "sauvegarde", "service", "veille", "demarrer" };

    /// <summary>Socle (sans arrêt, démarrage ni dossier de sauvegarde, que l'assistant appelle explicitement).</summary>
    public static readonly string[] Toutes =
        { "dossiers", "configuration", "taches", "parefeu", "base", "service", "veille" };

    /// <summary>Un échec de ces étapes n'empêche pas d'avoir un serveur fonctionnel : simple avertissement.</summary>
    private static readonly HashSet<string> NonBloquantes = new() { "taches", "veille" };

    /// <summary>« toutes », ou une liste séparée par des virgules ; toujours renvoyée dans l'ordre sûr (peu importe la saisie).</summary>
    public static List<string> AnalyserEtapes(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte) || texte.Trim().Equals("toutes", StringComparison.OrdinalIgnoreCase))
        {
            return Toutes.ToList();
        }

        var demandees = texte
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        foreach (var d in demandees)
        {
            if (!Ordre.Contains(d))
            {
                throw new ArgumentException($"Étape inconnue : « {d} » (valeurs possibles : {string.Join(", ", Ordre)}, toutes).");
            }
        }

        return Ordre.Where(demandees.Contains).ToList();
    }

    public static RapportMiseEnPlace Lancer(Demande d, IReadOnlyList<string> etapes)
    {
        var cible = d.VersionCible ?? Chemins.LireVersionApplication() ?? Constantes.VersionHote;
        var r = new RapportMiseEnPlace { VersionCible = cible };

        if (!VersionsApp.TryParse(cible, out _))
        {
            r.Erreurs.Add($"Version cible invalide : « {cible} ».");
            return Terminer(r);
        }

        if (etapes.Contains("base"))
        {
            r.Erreurs.AddRange(d.Valider());
        }

        var mode = ResoudreMode(d, cible, r);
        if (mode == null || r.Erreurs.Count > 0)
        {
            return Terminer(r);
        }

        r.Mode = mode.Value;
        r.PortHttp = d.PortHttp;

        if (r.Mode == ModeInstallation.Retrogradation)
        {
            r.Erreurs.Add("Le retour à une version plus ancienne que celle installée est refusé pour protéger vos données.");
            return Terminer(r);
        }

        JournalInstall.Ecrire($"Mise en place — mode {r.Mode}, version {cible}, étapes : {string.Join(", ", etapes)}");

        var echec = false;
        var rang = 0;

        foreach (var nom in etapes)
        {
            rang++;

            if (echec)
            {
                r.Etapes.Add(new EtapeResultat
                {
                    Nom = nom,
                    Statut = Statut.NonExecute,
                    Detail = "Non exécutée : une étape précédente a échoué."
                });
                continue;
            }

            JournalInstall.Ecrire($"[{rang}/{etapes.Count}] {nom}…");
            var chrono = Stopwatch.StartNew();

            EtapeResultat e;
            try
            {
                e = Executer(nom, d, r);
            }
            catch (Exception ex)
            {
                e = EtapeResultat.Echouee(ex.Message);
            }

            e.Nom = nom;
            e.DureeMs = chrono.ElapsedMilliseconds;

            if (e.Statut == Statut.Echec && NonBloquantes.Contains(nom))
            {
                r.Avertissements.Add($"L'étape « {nom} » a échoué sans empêcher la suite : {e.Detail}");
                e.Statut = Statut.Avertissement;
            }

            r.Etapes.Add(e);
            r.Avertissements.AddRange(e.Alertes);
            JournalInstall.Ecrire($"    → {e.Statut} : {e.Detail}");

            if (e.Statut == Statut.Echec)
            {
                echec = true;
                r.Erreurs.Add($"Étape « {nom} » : {e.Detail}");
            }
        }

        return Terminer(r);
    }

    private static EtapeResultat Executer(string nom, Demande d, RapportMiseEnPlace r) => nom switch
    {
        "arreter" => EtapeService.Arreter(),
        "dossiers" => EtapeDossiers.Executer(),
        "configuration" => EtapeConfiguration.Executer(d),
        "taches" => EtapeService.NettoyerTachesHeritees(),
        "parefeu" => EtapeParefeu.Executer(),
        "base" => EtapeBase.Executer(d, r),
        "sauvegarde" => EtapeSauvegarde.Executer(d),
        "service" => EtapeService.Installer(),
        "veille" => EtapeVeille.Executer(d),
        "demarrer" => EtapeService.Demarrer(),
        _ => throw new InvalidOperationException($"Étape inconnue : {nom}")
    };

    /// <summary>
    /// ATTENTION : « auto » ne fonctionne qu'AVANT la copie des fichiers. L'assistant doit donc décider le mode au tout début
    /// (commande detect) et le transmettre explicitement dans la demande. Ici, « auto » s'appuie sur version_installee.txt
    /// (écrit par le service après migration, jamais par la copie des fichiers), pas sur le fichier VERSION déjà remplacé.
    /// </summary>
    private static ModeInstallation? ResoudreMode(Demande d, string cible, RapportMiseEnPlace r)
    {
        switch ((d.Mode ?? "auto").Trim().ToLowerInvariant())
        {
            case "nouvelle": return ModeInstallation.Nouvelle;
            case "mise_a_jour": return ModeInstallation.MiseAJour;
            case "reparation": return ModeInstallation.Reparation;

            case "auto":
                string? migree = null;
                try
                {
                    if (File.Exists(Chemins.VersionInstallee)) migree = File.ReadAllText(Chemins.VersionInstallee).Trim();
                }
                catch
                {
                    // illisible : version inconnue
                }

                return DetectionInstallation.DeciderMode(migree, cible, EtapeService.Existe()).Mode;

            default:
                r.Erreurs.Add($"Mode inconnu : « {d.Mode} » (nouvelle, mise_a_jour, reparation ou auto).");
                return null;
        }
    }

    private static RapportMiseEnPlace Terminer(RapportMiseEnPlace r)
    {
        r.Ok = r.Erreurs.Count == 0 && !r.Etapes.Any(e => e.Statut == Statut.Echec);
        r.Journal = JournalInstall.Fichier;

        if (!r.Ok)
        {
            JournalInstall.Ecrire("Mise en place INCOMPLÈTE : " + string.Join(" | ", r.Erreurs));
        }

        return r;
    }
}