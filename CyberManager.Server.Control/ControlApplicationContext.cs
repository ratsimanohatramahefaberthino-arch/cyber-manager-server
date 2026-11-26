using Microsoft.Win32;
using CyberManager.Server.Common.Ipc;

namespace CyberManager.Server.Control;

/// <summary>
/// Cœur de la télécommande : icône de la zone de notification (menu, notifications), fenêtre, moniteur du service.
/// L'application vit tant que ce contexte vit : fermer la fenêtre ne quitte pas.
/// </summary>
internal sealed class ControlApplicationContext : ApplicationContext
{
    private readonly ParametresControle _parametres = ParametresControle.Charger();
    private readonly IconesEtat _icones = new();
    private readonly ServeurMonitor _moniteur = new();
    private readonly ActionsServeur _actions;
    private readonly MainForm _fenetre;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu = new();
    private readonly System.Windows.Forms.Control _marshal = new();

    private readonly ToolStripMenuItem _miEtat = new() { Enabled = false };
    private readonly ToolStripMenuItem _miOuvrir = new("Ouvrir Cyber Manager");
    private readonly ToolStripMenuItem _miAfficher = new("Afficher la télécommande");
    private readonly ToolStripMenuItem _miDemarrer = new("Démarrer le serveur");
    private readonly ToolStripMenuItem _miArreter = new("Arrêter le serveur…");
    private readonly ToolStripMenuItem _miRedemarrer = new("Redémarrer le serveur…");
    private readonly ToolStripMenuItem _miSauvegarde = new("Sauvegarder maintenant");
    private readonly ToolStripMenuItem _miCode = new("Copier le code d'installation des postes");
    private readonly ToolStripMenuItem _miAdresse = new("Copier l'adresse du serveur");
    private readonly ToolStripMenuItem _miDiagnostic = new("Exporter le diagnostic…");
    private readonly ToolStripMenuItem _miQuitter = new("Quitter la télécommande");

    private EventWaitHandle? _evenementAffichage;
    private RegisteredWaitHandle? _attenteAffichage;

    private Niveau? _niveauPrecedent;
    private Niveau? _niveauIcone;
    private string? _incidentNotifie;
    private bool _nettoye;

    public ControlApplicationContext(bool demarrerReduit)
    {
        _ = _marshal.Handle;   // crée le handle sur le thread d'interface (pour BeginInvoke depuis le réveil inter-instances)

        _actions = new ActionsServeur(_moniteur, () => FenetreVisible ? _fenetre : null);
        _fenetre = new MainForm(_moniteur, _actions, _parametres, _icones);
        _fenetre.ReduitDansZoneNotification += SurReduction;

        ConstruireMenu();

        _tray = new NotifyIcon
        {
            Icon = _icones.Pour(Niveau.Gris),
            Text = "Cyber Manager — connexion…",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _tray.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) AfficherFenetre();
        };
        _tray.BalloonTipClicked += (_, _) => AfficherFenetre();

        _actions.Retour += SurRetour;
        _moniteur.Changement += SurChangement;

        SystemEvents.SessionEnding += SurFinSession;
        ThreadExit += (_, _) => Nettoyer();

        EnregistrerReveil();

        _moniteur.Demarrer();

        if (!demarrerReduit) AfficherFenetre();
    }

    private bool FenetreVisible => _fenetre.Visible && _fenetre.WindowState != FormWindowState.Minimized;

    // ------------------------------------------------------------------
    // Fenêtre
    // ------------------------------------------------------------------

    private void AfficherFenetre()
    {
        if (!_fenetre.Visible) _fenetre.Show();
        if (_fenetre.WindowState == FormWindowState.Minimized) _fenetre.WindowState = FormWindowState.Normal;

        _fenetre.TopMost = true;    // astuce classique pour passer devant malgré le verrou du premier plan
        _fenetre.TopMost = false;
        _fenetre.Activate();
        _fenetre.BringToFront();
    }

    private void SurReduction()
    {
        _tray.ShowBalloonTip(6000, "Cyber Manager",
            "La télécommande reste active dans la zone de notification.\nLe serveur continue de fonctionner en arrière-plan.",
            ToolTipIcon.Info);
    }

    private void EnregistrerReveil()
    {
        try
        {
            _evenementAffichage = new EventWaitHandle(false, EventResetMode.AutoReset, Program.NomEvenement);
            _attenteAffichage = ThreadPool.RegisterWaitForSingleObject(
                _evenementAffichage,
                (_, _) =>
                {
                    try { _marshal.BeginInvoke(new Action(AfficherFenetre)); }
                    catch { /* l'application se ferme */ }
                },
                null, Timeout.Infinite, false);
        }
        catch (Exception ex)
        {
            JournalControle.Ecrire("Réveil inter-instances indisponible : " + ex.Message);
        }
    }

    // ------------------------------------------------------------------
    // Menu de la zone de notification
    // ------------------------------------------------------------------

    private void ConstruireMenu()
    {
        _miOuvrir.Font = new Font(_miOuvrir.Font, FontStyle.Bold);

        _menu.Items.AddRange(new ToolStripItem[]
        {
            _miEtat,
            new ToolStripSeparator(),
            _miOuvrir,
            _miAfficher,
            new ToolStripSeparator(),
            _miDemarrer,
            _miArreter,
            _miRedemarrer,
            new ToolStripSeparator(),
            _miSauvegarde,
            _miCode,
            _miAdresse,
            _miDiagnostic,
            new ToolStripSeparator(),
            _miQuitter
        });

        _miOuvrir.Click += (_, _) => _actions.OuvrirInterface();
        _miAfficher.Click += (_, _) => AfficherFenetre();
        _miDemarrer.Click += async (_, _) => await _actions.DemarrerAsync();
        _miArreter.Click += async (_, _) => await _actions.ArreterAsync();
        _miRedemarrer.Click += async (_, _) => await _actions.RedemarrerAsync();
        _miSauvegarde.Click += async (_, _) => await _actions.SauvegarderAsync();
        _miCode.Click += async (_, _) => await _actions.CopierCodeInstallationAsync();
        _miAdresse.Click += (_, _) => _actions.CopierAdresse();
        _miDiagnostic.Click += async (_, _) => await _actions.ExporterDiagnosticAsync();
        _miQuitter.Click += (_, _) => Quitter();

        _menu.Opening += (_, _) => RafraichirMenu();
    }

    private void RafraichirMenu()
    {
        var s = _moniteur.Courant;
        var vue = Presentation.Resumer(s);
        var d = Presentation.CalculerDroits(s, _actions.Occupe);

        _miEtat.Text = "Cyber Manager — " + vue.Titre;
        _miOuvrir.Enabled = d.Ouvrir;
        _miDemarrer.Enabled = d.Demarrer;
        _miArreter.Enabled = d.Arreter;
        _miRedemarrer.Enabled = d.Redemarrer;
        _miSauvegarde.Enabled = d.Sauvegarder;
        _miCode.Enabled = d.Code;
        _miAdresse.Enabled = d.CopierAdresse;
        _miDiagnostic.Enabled = !_actions.Occupe;
    }

    private void Quitter()
    {
        var reponse = MessageBox.Show(
            "Fermer la télécommande ?\n\n"
            + "Le serveur Cyber Manager continuera de fonctionner en arrière-plan. "
            + "Vous pourrez rouvrir la télécommande depuis le menu Démarrer.",
            "Quitter la télécommande", MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2);

        if (reponse != DialogResult.Yes) return;

        _fenetre.FermerDefinitivement();
        ExitThread();
    }

    // ------------------------------------------------------------------
    // Réaction aux changements d'état (icône, infobulle, notifications)
    // ------------------------------------------------------------------

    private void SurChangement(InstantaneServeur s)
    {
        var vue = Presentation.Resumer(s);

        if (_niveauIcone != vue.Niveau)
        {
            _niveauIcone = vue.Niveau;
            _tray.Icon = _icones.Pour(vue.Niveau);
        }

        _tray.Text = Raccourcir("Cyber Manager — " + vue.Titre, 60);

        // Nouvel incident de coupure (non acquitté) : notification unique
        if (s.Liaison == Liaison.Connecte && s.Statut is { } st && Presentation.EstIncident(st.Incident))
        {
            var cle = Presentation.CleIncident(st.Incident!);
            if (cle != _parametres.IncidentAcquitte && cle != _incidentNotifie)
            {
                _incidentNotifie = cle;
                _tray.ShowBalloonTip(10000, "Coupure détectée", Raccourcir(Presentation.TexteIncidentCourt(st), 240), ToolTipIcon.Warning);
            }
        }

        // Transitions de gravité (hors opérations demandées par le gérant lui-même)
        if (vue.Niveau != Niveau.Gris)
        {
            if (_niveauPrecedent != Niveau.Rouge && vue.Niveau == Niveau.Rouge && !_actions.Occupe)
            {
                _tray.ShowBalloonTip(10000, "Cyber Manager : " + vue.Titre, Raccourcir(vue.Message, 240), ToolTipIcon.Error);
            }
            else if (_niveauPrecedent == Niveau.Rouge && vue.Niveau == Niveau.Vert)
            {
                _tray.ShowBalloonTip(5000, "Cyber Manager", "Le serveur est de nouveau opérationnel.", ToolTipIcon.Info);
            }

            _niveauPrecedent = vue.Niveau;
        }
    }

    private void SurRetour(string message, bool erreur)
    {
        // Fenêtre cachée : le gérant ne verrait pas le retour sans une bulle
        if (FenetreVisible) return;

        _tray.ShowBalloonTip(6000, "Cyber Manager", Raccourcir(message, 240), erreur ? ToolTipIcon.Warning : ToolTipIcon.Info);
    }

    private static string Raccourcir(string texte, int max) =>
        texte.Length <= max ? texte : texte[..(max - 1)] + "…";

    // ------------------------------------------------------------------
    // Fin de session Windows et nettoyage
    // ------------------------------------------------------------------

    private void SurFinSession(object? sender, SessionEndingEventArgs e)
    {
        _fenetre.FermerDefinitivement();
        ExitThread();
    }

    private void Nettoyer()
    {
        if (_nettoye) return;
        _nettoye = true;

        SystemEvents.SessionEnding -= SurFinSession;

        try { _attenteAffichage?.Unregister(null); } catch { }
        try { _evenementAffichage?.Dispose(); } catch { }

        _moniteur.Dispose();

        _tray.Visible = false;   // évite l'icône fantôme dans la zone de notification
        _tray.Dispose();
        _menu.Dispose();

        if (!_fenetre.IsDisposed) _fenetre.Dispose();
        _marshal.Dispose();
        _icones.Dispose();
    }
}