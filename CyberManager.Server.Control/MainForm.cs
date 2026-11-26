using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using CyberManager.Server.Common;
using CyberManager.Server.Common.Ipc;
using Ctrl = System.Windows.Forms.Control;

namespace CyberManager.Server.Control;

internal sealed class MainForm : Form
{
    private readonly ServeurMonitor _moniteur;
    private readonly ActionsServeur _actions;
    private readonly ParametresControle _parametres;
    private readonly IconesEtat _icones;

    // Carte héro
    private readonly CarteControl _hero = new() { Height = 105 };
    private readonly VoyantControl _voyant = new() { Dock = DockStyle.Fill };
    private readonly Label _titre = new();
    private readonly Label _message = new();

    // Bandeau d'incident
    private readonly CarteControl _bandeau = new() { Height = 88 };
    private readonly Label _iconeBandeau = new();
    private readonly Label _titreBandeau = new();
    private readonly Label _texteBandeau = new();
    private readonly BoutonModerne _btnAcquitter = new();

    // Adresses
    private readonly ListBox _adresses = new();
    private readonly BoutonModerne _btnOuvrir = new();
    private readonly BoutonModerne _btnCopierAdresse = new();

    // Commandes
    private readonly BoutonModerne _btnDemarrer = new();
    private readonly BoutonModerne _btnArreter = new();
    private readonly BoutonModerne _btnRedemarrer = new();
    private readonly BoutonModerne _btnSauvegarde = new();
    private readonly BoutonModerne _btnCode = new();
    private readonly BoutonModerne _btnDiagnostic = new();

    // Tuiles de télémétrie
    private readonly TuileControl _tSessions = new() { Titre = "Sessions payantes en cours", Icone = Glyphes.Sessions, MiseEnAvant = true };
    private readonly TuileControl _tDuree = new() { Titre = "En marche depuis", Icone = Glyphes.Duree };
    private readonly TuileControl _tDisque = new() { Titre = "Espace disque libre", Icone = Glyphes.Disque };
    private readonly TuileControl _tWeb = new() { Titre = "Serveur web", Icone = Glyphes.Web };
    private readonly TuileControl _tPhp = new() { Titre = "Processus PHP", Icone = Glyphes.Php };
    private readonly TuileControl _tPlanif = new() { Titre = "Planificateur", Icone = Glyphes.Planificateur };
    private readonly TuileControl _tBase = new() { Titre = "Base de données", Icone = Glyphes.Base };
    private readonly TuileControl _tDecouverte = new() { Titre = "Découverte réseau", Icone = Glyphes.Decouverte };
    private readonly TuileControl _tSauvegarde = new() { Titre = "Dernière sauvegarde", Icone = Glyphes.Sauvegarde };
    private readonly TuileControl _tVersion = new() { Titre = "Version", Icone = Glyphes.Info };

    // Messages et pied de page
    private readonly CarteControl _zoneAvert = new() { Height = 56, Visible = false };
    private readonly Label _avertissements = new();
    private readonly CarteControl _zoneRetour = new() { Height = 42, Visible = false };
    private readonly Label _retour = new();
    private readonly Label _liaison = new();
    private readonly BoutonModerne _btnReparer = new();
    private readonly CheckBox _chkWindows = new();
    private readonly System.Windows.Forms.Timer _minuteurRetour = new() { Interval = 10000 };

    private Niveau? _niveauIcone;
    private string _cleAdresses = "\0";
    private bool _retourEstOccupation;
    private bool _fermetureReelle;
    private bool _initialisation = true;

    private enum TypeAdresse { Reseau, Local, Autre, Aucune }

    private sealed class EntreeAdresse
    {
        public string UrlBrute { get; }
        public TypeAdresse Type { get; }
        private readonly string _libelle;

        public EntreeAdresse(string url, TypeAdresse type, string? libelle = null)
        {
            UrlBrute = url;
            Type = type;
            _libelle = libelle ?? url;
        }

        public override string ToString() => _libelle;
    }

    public event Action? ReduitDansZoneNotification;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribut, ref int valeur, int taille);

    public MainForm(ServeurMonitor moniteur, ActionsServeur actions, ParametresControle parametres, IconesEtat icones)
    {
        _moniteur = moniteur;
        _actions = actions;
        _parametres = parametres;
        _icones = icones;

        Text = "Cyber Manager — Serveur";
        Font = Theme.Normale;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(820, 720);
        MinimumSize = new Size(760, 600);
        MaximizeBox = false;
        BackColor = Theme.Fond;
        Icon = (Icon)_icones.Pour(Niveau.Gris).Clone();

        Construire();

        _moniteur.Changement += Mettre;
        _actions.OccupeChange += SurOccupation;
        _actions.Retour += AfficherRetour;
        _minuteurRetour.Tick += (_, _) =>
        {
            _minuteurRetour.Stop();
            _zoneRetour.Visible = false;
        };

        _chkWindows.Checked = DemarrageWindows.EstActif();
        _initialisation = false;

        Mettre(_moniteur.Courant);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!Theme.Sombre) return;
        var un = 1;
        if (DwmSetWindowAttribute(Handle, 20, ref un, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(Handle, 19, ref un, sizeof(int));
        }
    }

    private static Label Etiquette(string texte, Font police, Color couleur, ContentAlignment alignement = ContentAlignment.MiddleLeft) => new()
    {
        Text = texte,
        Font = police,
        ForeColor = couleur,
        BackColor = Color.Transparent,
        AutoSize = false,
        AutoEllipsis = true,
        UseMnemonic = false,
        Dock = DockStyle.Fill,
        TextAlign = alignement
    };

    private static TableLayoutPanel Grille(int colonnes, int lignes) => new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = colonnes,
        RowCount = lignes,
        BackColor = Color.Transparent
    };

    private void Construire()
    {
        SuspendLayout();

        var defil = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(18, 16, 18, 10), BackColor = Theme.Fond };
        var racine = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = Color.Transparent
        };
        racine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        defil.Controls.Add(racine);
        Controls.Add(defil);

        void Ajouter(Ctrl c)
        {
            var ligne = racine.RowCount;
            racine.RowCount = ligne + 1;
            racine.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            c.Dock = DockStyle.Fill;
            c.Margin = new Padding(0, 0, 0, 14);
            racine.Controls.Add(c, 0, ligne);
        }

        // A. Carte héro : Voyant + Titre net (aucun bouton parasite)
        _hero.Padding = new Padding(14, 10, 16, 10);
        var hero = Grille(2, 1);
        hero.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));
        hero.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        hero.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var zoneTexte = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(8, 10, 8, 4) };
        _message.Dock = DockStyle.Fill;
        _message.Font = Theme.Normale;
        _message.ForeColor = Theme.Secondaire;
        _message.BackColor = Color.Transparent;
        _titre.Dock = DockStyle.Top;
        _titre.Height = 38;
        _titre.Font = Theme.TitreHero;
        _titre.BackColor = Color.Transparent;
        _titre.AutoEllipsis = true;
        _titre.TextAlign = ContentAlignment.MiddleLeft;
        zoneTexte.Controls.Add(_message);
        zoneTexte.Controls.Add(_titre);

        hero.Controls.Add(_voyant, 0, 0);
        hero.Controls.Add(zoneTexte, 1, 0);
        _hero.Controls.Add(hero);
        Ajouter(_hero);

        // Bandeau d'incident
        _bandeau.Padding = new Padding(12, 10, 12, 10);
        _bandeau.Visible = false;
        var bandeau = Grille(3, 1);
        bandeau.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46f));
        bandeau.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        bandeau.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116f));
        bandeau.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _iconeBandeau.Text = Glyphes.Avertissement;
        _iconeBandeau.Font = Theme.IconeGrande;
        _iconeBandeau.BackColor = Color.Transparent;
        _iconeBandeau.Dock = DockStyle.Fill;
        _iconeBandeau.TextAlign = ContentAlignment.TopCenter;

        var zoneBandeau = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        _texteBandeau.Dock = DockStyle.Fill;
        _texteBandeau.Font = Theme.Normale;
        _texteBandeau.BackColor = Color.Transparent;
        _titreBandeau.Dock = DockStyle.Top;
        _titreBandeau.Height = 24;
        _titreBandeau.Font = Theme.TitreCarte;
        _titreBandeau.BackColor = Color.Transparent;
        zoneBandeau.Controls.Add(_texteBandeau);
        zoneBandeau.Controls.Add(_titreBandeau);

        _btnAcquitter.Style = StyleBouton.Secondaire;
        _btnAcquitter.Icone = Glyphes.Coche;
        _btnAcquitter.Text = "Compris";
        _btnAcquitter.Size = new Size(104, 34);
        _btnAcquitter.Anchor = AnchorStyles.None;

        bandeau.Controls.Add(_iconeBandeau, 0, 0);
        bandeau.Controls.Add(zoneBandeau, 1, 0);
        bandeau.Controls.Add(_btnAcquitter, 2, 0);
        _bandeau.Controls.Add(bandeau);
        Ajouter(_bandeau);

        // B + C. Rangée Accès et Commandes (Hauteur portée à 240 pour zéro scrollbar)
        var rangee = Grille(2, 1);
        rangee.Height = 240;
        rangee.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        rangee.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        rangee.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        rangee.Controls.Add(ConstruireCarteAcces(), 0, 0);
        rangee.Controls.Add(ConstruireCarteCommandes(), 1, 0);
        Ajouter(rangee);

        // D. Télémétrie
        Ajouter(Section("État détaillé du système"));
        Ajouter(ConstruireTuiles());

        // Avertissements
        _zoneAvert.Padding = new Padding(14, 10, 14, 10);
        _zoneAvert.Teinte = Theme.Teinte(Theme.Ambre, 0.14f);
        _avertissements.Dock = DockStyle.Fill;
        _avertissements.Font = Theme.Normale;
        _avertissements.BackColor = Color.Transparent;
        _avertissements.ForeColor = Theme.Texte(Niveau.Orange);
        _zoneAvert.Controls.Add(_avertissements);
        Ajouter(_zoneAvert);

        _zoneRetour.Padding = new Padding(14, 0, 14, 0);
        _retour.Dock = DockStyle.Fill;
        _retour.Font = Theme.Gras;
        _retour.BackColor = Color.Transparent;
        _retour.TextAlign = ContentAlignment.MiddleLeft;
        _zoneRetour.Controls.Add(_retour);
        Ajouter(_zoneRetour);

        // Pied de page
        var pied = new TableLayoutPanel { Height = 42, ColumnCount = 3, RowCount = 1, BackColor = Color.Transparent };
        pied.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pied.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pied.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pied.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _liaison.Dock = DockStyle.Fill;
        _liaison.TextAlign = ContentAlignment.MiddleLeft;
        _liaison.ForeColor = Theme.Secondaire;
        _liaison.BackColor = Color.Transparent;
        _liaison.AutoEllipsis = true;

        _btnReparer.Style = StyleBouton.Secondaire;
        _btnReparer.Icone = Glyphes.Redemarrer;
        _btnReparer.Text = "Relancer le service";
        _btnReparer.Size = new Size(168, 34);
        _btnReparer.Anchor = AnchorStyles.None;
        _btnReparer.Margin = new Padding(8, 0, 8, 0);
        _btnReparer.Visible = false;

        _chkWindows.Text = "Lancer au démarrage de Windows";
        _chkWindows.AutoSize = true;
        _chkWindows.Cursor = Cursors.Hand;
        _chkWindows.Anchor = AnchorStyles.Right;
        _chkWindows.Margin = new Padding(8, 0, 0, 0);
        _chkWindows.BackColor = Color.Transparent;
        _chkWindows.ForeColor = Theme.Secondaire;

        pied.Controls.Add(_liaison, 0, 0);
        pied.Controls.Add(_btnReparer, 1, 0);
        pied.Controls.Add(_chkWindows, 2, 0);
        Ajouter(pied);

        // Événements
        _btnOuvrir.Click += (_, _) => _actions.OuvrirInterface(UrlChoisie());
        _adresses.DoubleClick += (_, _) => { if (_adresses.Enabled) _actions.OuvrirInterface(UrlChoisie()); };
        _btnCopierAdresse.Click += (_, _) => SurCopierAdresse();
        _btnDemarrer.Click += async (_, _) => await _actions.DemarrerAsync();
        _btnArreter.Click += async (_, _) => await _actions.ArreterAsync();
        _btnRedemarrer.Click += async (_, _) => await _actions.RedemarrerAsync();
        _btnSauvegarde.Click += async (_, _) => await _actions.SauvegarderAsync();
        _btnCode.Click += async (_, _) => await _actions.CopierCodeInstallationAsync();
        _btnDiagnostic.Click += async (_, _) => await _actions.ExporterDiagnosticAsync();
        _btnReparer.Click += async (_, _) => await _actions.ReparerAsync();
        _btnAcquitter.Click += (_, _) => Acquitter();
        _chkWindows.CheckedChanged += (_, _) => ChangerLancementWindows();

        ResumeLayout(true);
    }

    private static Label Section(string texte)
    {
        var l = Etiquette(texte.ToUpperInvariant(), Theme.PetiteGras, Theme.Secondaire);
        l.Height = 20;
        return l;
    }

    private CarteControl ConstruireCarteAcces()
    {
        var carte = new CarteControl { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 7, 0) };

        var t = Grille(1, 4);
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));

        t.Controls.Add(Etiquette("Adresse de connexion", Theme.TitreCarte, Theme.Titre), 0, 0);
        t.Controls.Add(Etiquette("Saisissez l'adresse Réseau sur les postes clients.", Theme.Petite, Theme.Secondaire), 0, 1);

        _adresses.Dock = DockStyle.Fill;
        _adresses.Margin = new Padding(0, 4, 0, 6);
        _adresses.IntegralHeight = false;
        _adresses.BorderStyle = BorderStyle.None;
        _adresses.BackColor = Theme.Carte;
        _adresses.Cursor = Cursors.Hand;
        _adresses.DrawMode = DrawMode.OwnerDrawFixed;
        _adresses.ItemHeight = 33; // Hauteur parfaite pour afficher toutes les adresses sans scrollbar
        _adresses.DrawItem += DessinerAdresse;
        t.Controls.Add(_adresses, 0, 2);

        var boutons = Grille(2, 1);
        boutons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        boutons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112f));
        boutons.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _btnOuvrir.Style = StyleBouton.Primaire;
        _btnOuvrir.Icone = Glyphes.Ouvrir;
        _btnOuvrir.Text = "Ouvrir Cyber Manager";
        _btnOuvrir.Dock = DockStyle.Fill;
        _btnOuvrir.Margin = new Padding(0, 0, 8, 0);

        _btnCopierAdresse.Style = StyleBouton.Secondaire;
        _btnCopierAdresse.Icone = Glyphes.Copier;
        _btnCopierAdresse.Text = "Copier";
        _btnCopierAdresse.Dock = DockStyle.Fill;
        _btnCopierAdresse.Margin = new Padding(0);

        boutons.Controls.Add(_btnOuvrir, 0, 0);
        boutons.Controls.Add(_btnCopierAdresse, 1, 0);
        t.Controls.Add(boutons, 0, 3);

        carte.Controls.Add(t);
        return carte;
    }

    private CarteControl ConstruireCarteCommandes()
    {
        var carte = new CarteControl { Dock = DockStyle.Fill, Margin = new Padding(7, 0, 0, 0) };

        var t = Grille(1, 5);
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        t.Controls.Add(Etiquette("Commandes du serveur", Theme.TitreCarte, Theme.Titre), 0, 0);

        var principaux = Grille(3, 1);
        for (var i = 0; i < 3; i++) principaux.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        principaux.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        Configurer(_btnDemarrer, "Démarrer", Glyphes.Lecture, StyleBouton.Succes, false, 0, 3);
        Configurer(_btnArreter, "Arrêter…", Glyphes.Arret, StyleBouton.Danger, false, 1, 3);
        Configurer(_btnRedemarrer, "Redémarrer…", Glyphes.Redemarrer, StyleBouton.Secondaire, false, 2, 3);
        principaux.Controls.Add(_btnDemarrer, 0, 0);
        principaux.Controls.Add(_btnArreter, 1, 0);
        principaux.Controls.Add(_btnRedemarrer, 2, 0);
        t.Controls.Add(principaux, 0, 1);

        var libelle = Etiquette("OUTILS ET MAINTENANCE", Theme.PetiteGras, Theme.Secondaire, ContentAlignment.BottomLeft);
        libelle.Padding = new Padding(0, 0, 0, 2);
        t.Controls.Add(libelle, 0, 2);

        var outils = Grille(3, 1);
        for (var i = 0; i < 3; i++) outils.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        outils.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        Configurer(_btnSauvegarde, "Sauvegarder", Glyphes.Sauvegarde, StyleBouton.Secondaire, true, 0, 3);
        Configurer(_btnCode, "Code postes", Glyphes.Cle, StyleBouton.Secondaire, true, 1, 3);
        Configurer(_btnDiagnostic, "Diagnostic…", Glyphes.Diagnostic, StyleBouton.Secondaire, true, 2, 3);
        outils.Controls.Add(_btnSauvegarde, 0, 0);
        outils.Controls.Add(_btnCode, 1, 0);
        outils.Controls.Add(_btnDiagnostic, 2, 0);
        t.Controls.Add(outils, 0, 3);

        carte.Controls.Add(t);
        return carte;
    }

    private static void Configurer(BoutonModerne b, string texte, string icone, StyleBouton style, bool compact, int index, int total)
    {
        b.Text = texte;
        b.Icone = icone;
        b.Style = style;
        b.Compact = compact;
        b.Dock = DockStyle.Fill;
        b.Margin = new Padding(index == 0 ? 0 : 3, compact ? 2 : 3, index == total - 1 ? 0 : 3, compact ? 2 : 3);
    }

    private TableLayoutPanel ConstruireTuiles()
    {
        var g = Grille(7, 5);
        g.Height = 3 * 84 + 2 * 10;
        for (var c = 0; c < 7; c++)
        {
            g.ColumnStyles.Add(c % 2 == 0 ? new ColumnStyle(SizeType.Percent, 25f) : new ColumnStyle(SizeType.Absolute, 10f));
        }

        for (var r = 0; r < 5; r++)
        {
            g.RowStyles.Add(r % 2 == 0 ? new RowStyle(SizeType.Absolute, 84f) : new RowStyle(SizeType.Absolute, 10f));
        }

        void Poser(TuileControl t, int colonne, int ligne, int etendue = 1)
        {
            t.Dock = DockStyle.Fill;
            t.Margin = new Padding(0);
            g.Controls.Add(t, colonne, ligne);
            if (etendue > 1) g.SetColumnSpan(t, etendue);
        }

        Poser(_tSessions, 0, 0, 3);
        Poser(_tDuree, 4, 0);
        Poser(_tDisque, 6, 0);

        Poser(_tWeb, 0, 2);
        Poser(_tPhp, 2, 2);
        Poser(_tPlanif, 4, 2);
        Poser(_tBase, 6, 2);

        Poser(_tDecouverte, 0, 4);
        Poser(_tSauvegarde, 2, 4);
        Poser(_tVersion, 4, 4, 3);

        return g;
    }

    private void DessinerAdresse(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;

        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using (var fond = new SolidBrush(_adresses.BackColor)) g.FillRectangle(fond, e.Bounds);

        var objet = _adresses.Items[e.Index];
        var r = new Rectangle(e.Bounds.X + 1, e.Bounds.Y + 2, e.Bounds.Width - 3, e.Bounds.Height - 4);
        var selection = (e.State & DrawItemState.Selected) != 0 && _adresses.Enabled;

        if (selection)
        {
            using var chemin = Theme.Arrondi(r, 8);
            using var pinceau = new SolidBrush(Theme.Teinte(Theme.Violet, 0.12f));
            using var stylo = new Pen(Color.FromArgb(130, Theme.Violet), 1);
            g.FillPath(pinceau, chemin);
            g.DrawPath(stylo, chemin);
        }

        const TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine
                                  | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;

        if (objet is not EntreeAdresse item || item.Type == TypeAdresse.Aucune)
        {
            TextRenderer.DrawText(g, objet.ToString() ?? "", Theme.Normale,
                new Rectangle(r.X + 12, r.Y, r.Width - 24, r.Height), Theme.Secondaire, f);
            return;
        }

        var (pilule, niveau) = item.Type switch
        {
            TypeAdresse.Reseau => ("RÉSEAU", Niveau.Vert),
            TypeAdresse.Local => ("CE PC", Niveau.Gris),
            _ => ("RÉSEAU", Niveau.Vert)
        };

        var taillePilule = TextRenderer.MeasureText(g, pilule, Theme.PetiteGras, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        var cadre = new RectangleF(r.X + 8, r.Y + (r.Height - 18) / 2f, taillePilule.Width + 14, 18);

        using (var chemin = Theme.Arrondi(cadre, 9))
        using (var pinceau = new SolidBrush(Theme.Melanger(Theme.Carte, Theme.Accent(niveau), 0.18f)))
        {
            g.FillPath(pinceau, chemin);
        }

        TextRenderer.DrawText(g, pilule, Theme.PetiteGras, Rectangle.Round(cadre), Theme.Texte(niveau),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var xUrl = (int)cadre.Right + 8;
        TextRenderer.DrawText(g, item.UrlBrute, Theme.Code, new Rectangle(xUrl, r.Y, Math.Max(0, r.Right - xUrl - 6), r.Height),
            Theme.Titre, f);
    }

    private static TypeAdresse Classer(string url)
    {
        if (Presentation.EstBoucleLocale(url)) return TypeAdresse.Local;
        return TypeAdresse.Reseau;
    }

    private void Mettre(InstantaneServeur s)
    {
        if (IsDisposed) return;

        var vue = Presentation.Resumer(s);
        var st = s.Statut;

        _voyant.Niveau = vue.Niveau;
        _hero.Teinte = vue.Niveau == Niveau.Gris ? null : Theme.Teinte(Theme.Accent(vue.Niveau), 0.09f);
        _titre.Text = vue.Titre;
        _titre.ForeColor = vue.Niveau == Niveau.Gris ? Theme.Titre : Theme.Texte(vue.Niveau);
        _message.Text = vue.Message;

        if (_niveauIcone != vue.Niveau)
        {
            _niveauIcone = vue.Niveau;
            Icon = (Icon)_icones.Pour(vue.Niveau).Clone();
        }

        MettreBandeau(s);
        MettreAdresses(st);
        MettreDetails(st);
        MettreAvertissements(st);
        MettrePied(s);
        MettreBoutons(s);
    }

    private void MettreBandeau(InstantaneServeur s)
    {
        var st = s.Statut;

        if (s.Liaison == Liaison.Connecte && st != null && Presentation.EstIncident(st.Incident)
            && Presentation.CleIncident(st.Incident!) != _parametres.IncidentAcquitte)
        {
            var horloge = Presentation.EstHorloge(st.Incident!);
            var niveau = horloge ? Niveau.Rouge : Niveau.Orange;

            _bandeau.Teinte = Theme.Teinte(Theme.Accent(niveau), 0.14f);
            _iconeBandeau.ForeColor = Theme.Texte(niveau);
            _titreBandeau.ForeColor = Theme.Texte(niveau);
            _texteBandeau.ForeColor = Theme.Texte(niveau);
            _titreBandeau.Text = horloge ? "Vérification de l'horloge système" : "Coupure électrique détectée";
            _texteBandeau.Text = Presentation.TexteIncident(st);
            _bandeau.Visible = true;
        }
        else
        {
            _bandeau.Visible = false;
        }
    }

    private void MettreAdresses(StatutServeur? st)
    {
        var urls = st?.Urls.Distinct().ToList() ?? new List<string>();
        var cle = string.Join("|", urls);
        if (cle == _cleAdresses) return;

        var selectionPrecedente = UrlChoisie();

        _adresses.BeginUpdate();
        _adresses.Items.Clear();

        if (urls.Count == 0)
        {
            _adresses.Items.Add(new EntreeAdresse("", TypeAdresse.Aucune, "Adresse disponible dès la fin du démarrage"));
            _adresses.Enabled = false;
        }
        else
        {
            _adresses.Enabled = true;
            EntreeAdresse? aSelectionner = null;
            var meilleure = Presentation.MeilleureUrl(st);

            foreach (var u in urls)
            {
                var item = new EntreeAdresse(u, Classer(u));
                _adresses.Items.Add(item);

                if (selectionPrecedente != null && u == selectionPrecedente)
                {
                    aSelectionner = item;
                }
                else if (aSelectionner == null && u == meilleure)
                {
                    aSelectionner = item;
                }
            }

            _adresses.SelectedItem = aSelectionner ?? (_adresses.Items.Count > 0 ? _adresses.Items[0] : null);
        }

        _adresses.EndUpdate();
        _adresses.Invalidate();
        _cleAdresses = cle;
    }

    private void MettreDetails(StatutServeur? st)
    {
        if (st == null)
        {
            foreach (var t in new[] { _tWeb, _tPhp, _tPlanif, _tBase, _tDecouverte, _tSessions, _tSauvegarde, _tDisque, _tVersion, _tDuree })
            {
                t.Definir("—", Niveau.Gris);
            }
            return;
        }

        var arrete = st.Etat == EtatServeur.Arrete;

        var (texte, niveau) = ResumeProcessus(st.Caddy, st.PortHttp is int p ? $" · port {p}" : "");
        _tWeb.Definir(texte, niveau);

        if (st.WorkersTotal == 0) _tPhp.Definir("—", Niveau.Gris);
        else if (st.WorkersActifs == st.WorkersTotal) _tPhp.Definir($"{st.WorkersActifs}/{st.WorkersTotal} actifs", Niveau.Vert);
        else if (st.WorkersActifs == 0) _tPhp.Definir($"0/{st.WorkersTotal} actif", arrete ? Niveau.Gris : Niveau.Rouge);
        else _tPhp.Definir($"{st.WorkersActifs}/{st.WorkersTotal} actifs", Niveau.Orange);

        (texte, niveau) = ResumeProcessus(st.Planificateur, "");
        if (niveau == Niveau.Vert && st.PlanificateurDernierPassageSecondes is double age)
        {
            texte = $"Actif · il y a {Presentation.DureeCourte(age)}";
            if (age > 60) niveau = Niveau.Orange;
        }

        _tPlanif.Definir(texte, niveau);
        _tBase.Definir(st.BaseOk ? "OK" : "Erreur", st.BaseOk ? Niveau.Vert : Niveau.Rouge);

        (texte, niveau) = ResumeProcessus(st.Decouverte, st.PortUdpDecouverte is int udp ? $" · UDP {udp}" : "");
        _tDecouverte.Definir(texte, niveau);

        _tSessions.Definir(st.SessionsEnCours?.ToString(CultureInfo.InvariantCulture) ?? "0", Niveau.Gris);

        if (st.DerniereSauvegarde is { } sauvegarde)
        {
            var ancienne = DateTimeOffset.Now - sauvegarde > TimeSpan.FromHours(48);
            _tSauvegarde.Definir(Presentation.DateHeure(sauvegarde), ancienne ? Niveau.Orange : Niveau.Vert);
        }
        else
        {
            _tSauvegarde.Definir("Aucune", Niveau.Gris);
        }

        if (st.EspaceDisqueLibreMo is long mo)
        {
            _tDisque.Definir(Presentation.Taille(mo), mo < 500 ? Niveau.Rouge : mo < 2048 ? Niveau.Orange : Niveau.Gris);
        }
        else
        {
            _tDisque.Definir("—", Niveau.Gris);
        }

        _tVersion.Definir($"App {st.VersionApplication ?? "3.1.0"} · Service {st.VersionHote}", Niveau.Gris);
        _tDuree.Definir(arrete ? "—" : Presentation.Duree(st.DureeFonctionnementSecondes), Niveau.Gris);
    }

    private static (string Texte, Niveau Niveau) ResumeProcessus(ProcessusStatut? p, string complement)
    {
        if (p == null) return ("—", Niveau.Gris);

        return p.Phase switch
        {
            PhaseProcessus.EnCours when p.Vivant => ("En marche" + complement, Niveau.Vert),
            PhaseProcessus.EnCours => ("Ne répond plus", Niveau.Orange),
            PhaseProcessus.Temporisation => ("Redémarrage en cours", Niveau.Orange),
            PhaseProcessus.EnPanne => ("En panne", Niveau.Rouge),
            _ => ("Arrêté", Niveau.Gris)
        };
    }

    private void MettreAvertissements(StatutServeur? st)
    {
        var liste = st?.Avertissements.Where(a => !string.IsNullOrWhiteSpace(a)).Take(4).ToList();
        if (liste == null || liste.Count == 0)
        {
            _zoneAvert.Visible = false;
            return;
        }

        _avertissements.Text = "Attention :\n• " + string.Join("\n• ", liste);
        _zoneAvert.Visible = true;
    }

    private void MettrePied(InstantaneServeur s)
    {
        switch (s.Liaison)
        {
            case Liaison.Connecte:
                _liaison.Text = "Service opérationnel · synchronisé à " + (s.DernierSuccesLocal ?? DateTime.Now).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                _liaison.ForeColor = Theme.Secondaire;
                break;
            case Liaison.Connexion:
            case Liaison.Reconnexion:
                _liaison.Text = "Reconnexion automatique…";
                _liaison.ForeColor = Theme.Texte(Niveau.Orange);
                break;
            default:
                _liaison.Text = "Service indisponible : " + (string.IsNullOrWhiteSpace(s.Detail) ? "cliquez sur Réparer." : s.Detail);
                _liaison.ForeColor = Theme.Texte(Niveau.Rouge);
                break;
        }

        _btnReparer.Visible = s.Liaison is Liaison.ServiceAbsent or Liaison.ServiceArrete or Liaison.Injoignable;
    }

    private void MettreBoutons(InstantaneServeur s)
    {
        var d = Presentation.CalculerDroits(s, _actions.Occupe);
        var st = s.Statut;
        var estEnMarche = s.Liaison == Liaison.Connecte && st != null && st.Etat != EtatServeur.Arrete;

        _btnOuvrir.Enabled = d.Ouvrir;
        _btnCopierAdresse.Enabled = d.CopierAdresse;
        _btnDemarrer.Enabled = d.Demarrer && !estEnMarche;
        _btnArreter.Enabled = d.Arreter && estEnMarche;
        _btnRedemarrer.Enabled = d.Redemarrer && estEnMarche;
        _btnSauvegarde.Enabled = d.Sauvegarder;
        _btnCode.Enabled = d.Code;
        _btnDiagnostic.Enabled = !_actions.Occupe;
        _btnReparer.Enabled = !_actions.Occupe;
    }

    private async void SurCopierAdresse()
    {
        var url = UrlChoisie();
        if (string.IsNullOrWhiteSpace(url)) return;

        _actions.CopierAdresse(url);

        _btnCopierAdresse.Style = StyleBouton.Succes;
        _btnCopierAdresse.Icone = Glyphes.Coche;
        _btnCopierAdresse.Text = "Copié !";

        await Task.Delay(1800);

        if (!IsDisposed)
        {
            _btnCopierAdresse.Style = StyleBouton.Secondaire;
            _btnCopierAdresse.Icone = Glyphes.Copier;
            _btnCopierAdresse.Text = "Copier";
        }
    }

    private void SurOccupation()
    {
        if (IsDisposed) return;

        if (_actions.Occupe)
        {
            _retourEstOccupation = true;
            _minuteurRetour.Stop();
            _zoneRetour.Teinte = Theme.Teinte(Theme.Violet, 0.10f);
            _retour.Text = "⏳ " + _actions.OperationEnCours;
            _retour.ForeColor = Theme.VioletTexte;
            _zoneRetour.Visible = true;
        }
        else if (_retourEstOccupation)
        {
            _retourEstOccupation = false;
            _zoneRetour.Visible = false;
        }

        MettreBoutons(_moniteur.Courant);
    }

    private void AfficherRetour(string message, bool erreur)
    {
        if (IsDisposed) return;

        var niveau = erreur ? Niveau.Rouge : Niveau.Vert;
        _retourEstOccupation = false;
        _zoneRetour.Teinte = Theme.Teinte(Theme.Accent(niveau), 0.14f);
        _retour.Text = (erreur ? "✖ " : "✔ ") + message;
        _retour.ForeColor = Theme.Texte(niveau);
        _zoneRetour.Visible = true;

        _minuteurRetour.Stop();
        _minuteurRetour.Start();
    }

    private string? UrlChoisie()
    {
        if (!_adresses.Enabled) return null;
        if (_adresses.SelectedItem is EntreeAdresse item) return item.UrlBrute;
        return _adresses.SelectedItem as string;
    }

    private void Acquitter()
    {
        var incident = _moniteur.Courant.Statut?.Incident;
        if (incident == null) return;

        _parametres.IncidentAcquitte = Presentation.CleIncident(incident);
        _parametres.Enregistrer();
        _bandeau.Visible = false;
    }

    private void ChangerLancementWindows()
    {
        if (_initialisation) return;
        try { DemarrageWindows.Definir(_chkWindows.Checked); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Impossible de modifier le démarrage : " + ex.Message, "Cyber Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !_fermetureReelle)
        {
            e.Cancel = true;
            Hide();
            ReduitDansZoneNotification?.Invoke();
            return;
        }
        base.OnFormClosing(e);
    }

    public void FermerDefinitivement()
    {
        _fermetureReelle = true;
        Close();
    }
}