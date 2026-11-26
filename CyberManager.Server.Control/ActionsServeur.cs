using System.ComponentModel;
using System.Diagnostics;
using CyberManager.Server.Common;
using CyberManager.Server.Common.Ipc;

namespace CyberManager.Server.Control;

internal sealed class ActionsServeur
{
    private const string NomInstallateur = "CyberManagerServerSetup.exe";

    private readonly ServeurMonitor _moniteur;
    private readonly Func<IWin32Window?> _proprietaire;
    private int _occupe;

    public event Action? OccupeChange;
    public event Action<string, bool>? Retour;

    public bool Occupe => Volatile.Read(ref _occupe) != 0;
    public string? OperationEnCours { get; private set; }

    public ActionsServeur(ServeurMonitor moniteur, Func<IWin32Window?> proprietaire)
    {
        _moniteur = moniteur;
        _proprietaire = proprietaire;
    }

    public Task DemarrerAsync() => Garde(nameof(DemarrerAsync), async () =>
    {
        if (!Entrer("Démarrage du serveur en cours…")) return;

        try
        {
            var r = await PipeClient.EnvoyerAsync(Commandes.Start, false, TimeSpan.FromMinutes(4));
            Resultat(r, "Le serveur est désormais opérationnel.");
        }
        finally
        {
            Sortir();
        }
    });

    public Task ArreterAsync() => Garde(nameof(ArreterAsync), () => ArretOuRedemarrageAsync(false));

    public Task RedemarrerAsync() => Garde(nameof(RedemarrerAsync), () => ArretOuRedemarrageAsync(true));

    private async Task ArretOuRedemarrageAsync(bool redemarrer)
    {
        if (Occupe)
        {
            Retour?.Invoke("Une opération est déjà en cours, patientez un instant.", false);
            return;
        }

        var etat = await PipeClient.EnvoyerAsync(Commandes.GetStatus, false, TimeSpan.FromSeconds(5));
        if (!etat.Ok)
        {
            Afficher("Le service ne répond pas. Impossible de vérifier les sessions clientes en cours.", MessageBoxIcon.Warning);
            return;
        }

        if (!Confirmer(redemarrer, etat.Reponse?.Statut?.SessionsEnCours)) return;

        if (!Entrer(redemarrer ? "Redémarrage du serveur en cours…" : "Arrêt du serveur en cours…")) return;

        try
        {
            var r = await PipeClient.EnvoyerAsync(redemarrer ? Commandes.Restart : Commandes.Stop, true, TimeSpan.FromMinutes(4));
            Resultat(r, redemarrer ? "Le serveur a été redémarré avec succès." : "Le serveur a été arrêté proprement.");
        }
        finally
        {
            Sortir();
        }
    }

    private bool Confirmer(bool redemarrer, int? sessions)
    {
        var verbe = redemarrer ? "redémarrer" : "arrêter";
        string texte;
        MessageBoxIcon icone;

        if (sessions is > 0)
        {
            var phrase = sessions == 1
                ? "ATTENTION : 1 session payante est actuellement en cours !"
                : $"ATTENTION : {sessions} sessions payantes sont actuellement en cours !";

            texte = $"{phrase}\n\nLe temps sera figé mais les clients seront temporairement déconnectés de leur poste.\n\nVoulez-vous vraiment {verbe} le serveur maintenant ?";
            icone = MessageBoxIcon.Warning;
        }
        else
        {
            texte = redemarrer
                ? "Aucune session en cours.\nLe serveur va redémarrer en quelques secondes.\n\nConfirmer le redémarrage ?"
                : "Aucune session en cours.\nLes postes ne pourront plus se connecter tant que le serveur sera coupé.\n\nConfirmer l'arrêt du serveur ?";
            icone = MessageBoxIcon.Question;
        }

        var titre = redemarrer ? "Confirmation — Redémarrage du serveur" : "Confirmation — Arrêt du serveur";

        return MessageBox.Show(_proprietaire(), texte, titre, MessageBoxButtons.YesNo, icone, MessageBoxDefaultButton.Button2)
               == DialogResult.Yes;
    }

    public Task SauvegarderAsync() => Garde(nameof(SauvegarderAsync), async () =>
    {
        if (!Entrer("Création d'une sauvegarde de la base de données…")) return;

        try
        {
            var r = await PipeClient.EnvoyerAsync(Commandes.BackupNow, false, TimeSpan.FromMinutes(5));
            Resultat(r, "Sauvegarde de sécurité créée avec succès.");
        }
        finally
        {
            Sortir();
        }
    });

    public Task CopierCodeInstallationAsync() => Garde(nameof(CopierCodeInstallationAsync), async () =>
    {
        var r = await PipeClient.EnvoyerAsync(Commandes.GetInstallCode, false, TimeSpan.FromSeconds(15));

        if (r.Ok && !string.IsNullOrWhiteSpace(r.Reponse?.CodeInstallation))
        {
            if (CopierPressePapiers(r.Reponse!.CodeInstallation!))
            {
                Retour?.Invoke("Code d'installation des postes copié dans le presse-papiers.", false);
            }
            return;
        }

        Afficher("Impossible de récupérer le code d'installation : " + r.Message, MessageBoxIcon.Warning);
    });

    public void OuvrirInterface(string? url = null)
    {
        try
        {
            url ??= Presentation.MeilleureUrl(_moniteur.Courant.Statut);

            if (string.IsNullOrWhiteSpace(url))
            {
                Afficher("L'adresse du serveur n'est pas encore disponible : patientez que le voyant passe au vert.", MessageBoxIcon.Information);
                return;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                Afficher("Adresse invalide : " + url, MessageBoxIcon.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            JournalControle.Ecrire("Ouverture interface : " + ex);
            Afficher("Impossible d'ouvrir le navigateur : " + ex.Message, MessageBoxIcon.Warning);
        }
    }

    public void CopierAdresse(string? url = null)
    {
        url ??= Presentation.MeilleureUrl(_moniteur.Courant.Statut);

        if (string.IsNullOrWhiteSpace(url))
        {
            Afficher("L'adresse n'est pas encore disponible.", MessageBoxIcon.Information);
            return;
        }

        if (CopierPressePapiers(url))
        {
            Retour?.Invoke("Adresse copiée : " + url, false);
        }
    }

    private bool CopierPressePapiers(string texte)
    {
        try
        {
            Clipboard.SetDataObject(texte, true, 5, 100);
            return true;
        }
        catch (Exception ex)
        {
            JournalControle.Ecrire("Presse-papiers : " + ex.Message);
            return false;
        }
    }

    public Task ExporterDiagnosticAsync() => Garde(nameof(ExporterDiagnosticAsync), async () =>
    {
        if (Occupe) return;

        using var boite = new SaveFileDialog
        {
            Title = "Exporter le diagnostic système",
            Filter = "Archive ZIP (*.zip)|*.zip",
            FileName = $"diagnostic-cybermanager-{DateTime.Now:yyyyMMdd-HHmm}.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            OverwritePrompt = true
        };

        if (boite.ShowDialog(_proprietaire()) != DialogResult.OK) return;

        var instantane = _moniteur.Courant;
        var chemin = boite.FileName;

        if (!Entrer("Génération de l'archive diagnostic…")) return;

        try
        {
            await Task.Run(() => DiagnosticExporter.Exporter(chemin, instantane));
            Retour?.Invoke("Diagnostic exporté sur le Bureau.", false);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{chemin}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            JournalControle.Ecrire("Export diagnostic : " + ex);
            Afficher("L'exportation a échoué : " + ex.Message, MessageBoxIcon.Warning);
        }
        finally
        {
            Sortir();
        }
    });

    public Task ReparerAsync() => Garde(nameof(ReparerAsync), async () =>
    {
        if (Occupe) return;

        if (_moniteur.Courant.Liaison == Liaison.ServiceAbsent)
        {
            LancerInstallateur();
            return;
        }

        var accord = MessageBox.Show(
            _proprietaire(),
            "Windows va demander l'accord administrateur pour redémarrer le service de gestion.\n\nContinuer ?",
            "Relance du service", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (accord != DialogResult.Yes) return;

        if (!Entrer("Redémarrage du service Windows…")) return;

        try
        {
            var demarrage = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"Restart-Service -Name '{Constantes.NomService}' -Force -ErrorAction Stop\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var p = Process.Start(demarrage);
            if (p != null) await p.WaitForExitAsync();
            Retour?.Invoke("Service Windows relancé avec succès.", false);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Retour?.Invoke("Action annulée par l'administrateur.", true);
        }
        finally
        {
            Sortir();
        }
    });

    private void LancerInstallateur()
    {
        var candidats = new[]
        {
            Path.Combine(AppContext.BaseDirectory, NomInstallateur),
            Path.Combine(Chemins.Code, NomInstallateur),
            Path.Combine(Chemins.Code, "installer", NomInstallateur)
        };

        var trouve = candidats.FirstOrDefault(File.Exists);
        if (trouve != null)
        {
            Process.Start(new ProcessStartInfo(trouve) { UseShellExecute = true, Verb = "runas" });
        }
    }

    private void Resultat(ResultatPipe r, string succesParDefaut)
    {
        if (r.Ok)
        {
            Retour?.Invoke(string.IsNullOrWhiteSpace(r.Reponse?.Message) ? succesParDefaut : r.Reponse!.Message!, false);
        }
        else
        {
            Afficher(r.Message, MessageBoxIcon.Warning);
        }
    }

    private bool Entrer(string operation)
    {
        if (Interlocked.CompareExchange(ref _occupe, 1, 0) != 0) return false;
        OperationEnCours = operation;
        OccupeChange?.Invoke();
        return true;
    }

    private void Sortir()
    {
        OperationEnCours = null;
        Interlocked.Exchange(ref _occupe, 0);
        OccupeChange?.Invoke();
        _moniteur.Actualiser();
    }

    private async Task Garde(string nom, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            JournalControle.Ecrire($"{nom} : {ex}");
            Afficher("Erreur : " + ex.Message, MessageBoxIcon.Error);
        }
    }

    private void Afficher(string texte, MessageBoxIcon icone) =>
        MessageBox.Show(_proprietaire(), texte, "Cyber Manager", MessageBoxButtons.OK, icone);
}