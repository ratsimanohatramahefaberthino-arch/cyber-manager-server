using System.Drawing.Drawing2D;
using CyberManager.Server.Common.Ipc;
using Ctrl = System.Windows.Forms.Control;

namespace CyberManager.Server.Control;

/// <summary>Carte à coins arrondis, bordure fine et ombre douce.</summary>
internal sealed class CarteControl : Panel
{
    private Color? _teinte;

    public CarteControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Fond;
        Padding = new Padding(16);
    }

    public int Rayon { get; set; } = 12;

    public Color? Teinte
    {
        get => _teinte;
        set
        {
            if (_teinte == value) return;
            _teinte = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new RectangleF(0.5f, 0.5f, Width - 2f, Height - 4f);

        using (var ombre = Theme.Arrondi(new RectangleF(rect.X, rect.Y + 1.5f, rect.Width, rect.Height), Rayon))
        using (var pinceauOmbre = new SolidBrush(Color.FromArgb(Theme.Sombre ? 70 : 14, 15, 23, 42)))
        {
            g.FillPath(pinceauOmbre, ombre);
        }

        var fond = _teinte ?? Theme.Carte;
        var bord = _teinte is null ? Theme.Bordure : Theme.Melanger(fond, Theme.Secondaire, 0.22f);

        using var chemin = Theme.Arrondi(rect, Rayon);
        using var pinceau = new SolidBrush(fond);
        using var stylo = new Pen(bord, 1);
        g.FillPath(pinceau, chemin);
        g.DrawPath(stylo, chemin);
    }
}

internal enum StyleBouton { Primaire, Succes, Danger, Secondaire, Discret }

/// <summary>Bouton plat arrondi moderne avec icône Segoe MDL2 Assets.</summary>
internal sealed class BoutonModerne : Button
{
    private bool _survol;
    private bool _appuye;
    private StyleBouton _style = StyleBouton.Secondaire;
    private string _icone = "";
    private bool _compact;

    public BoutonModerne()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Font = Theme.Gras;
    }

    public StyleBouton Style
    {
        get => _style;
        set { _style = value; Invalidate(); }
    }

    public string Icone
    {
        get => _icone;
        set { _icone = value; Invalidate(); }
    }

    public bool Compact
    {
        get => _compact;
        set { _compact = value; Font = value ? Theme.Compacte : Theme.Gras; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e) { _survol = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _survol = false; _appuye = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _appuye = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _appuye = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        _survol = false;
        _appuye = false;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color fond, bord, texte;

        if (!Enabled)
        {
            fond = Theme.Melanger(Theme.Carte, Theme.Secondaire, 0.08f);
            bord = Theme.Bordure;
            texte = Theme.TexteDesactive;
        }
        else
        {
            switch (_style)
            {
                case StyleBouton.Primaire:
                    fond = _survol ? Theme.VioletSurvol : Theme.Violet;
                    bord = fond;
                    texte = Color.White;
                    break;

                case StyleBouton.Succes:
                    fond = _survol ? Theme.EmeraudeFonce : Theme.Emeraude;
                    bord = fond;
                    texte = Color.White;
                    break;

                case StyleBouton.Danger:
                    fond = _survol ? Theme.Teinte(Theme.Rouge, 0.14f) : Theme.Carte;
                    bord = Theme.Melanger(Theme.Carte, Theme.Rouge, 0.45f);
                    texte = Theme.Texte(Niveau.Rouge);
                    break;

                case StyleBouton.Discret:
                    fond = _survol ? Theme.Survol : Color.FromArgb(0, Theme.Carte);
                    bord = Color.Transparent;
                    texte = Theme.Secondaire;
                    break;

                default:
                    fond = _survol ? Theme.Survol : Theme.Carte;
                    bord = Theme.Bordure;
                    texte = Theme.Titre;
                    break;
            }

            if (_appuye && fond.A > 0) fond = Theme.Melanger(fond, Color.Black, 0.10f);
        }

        var r = new RectangleF(0.5f, 0.5f, Width - 2f, Height - 2f);
        using (var chemin = Theme.Arrondi(r, 8))
        {
            if (fond.A > 0)
            {
                using var pinceau = new SolidBrush(fond);
                g.FillPath(pinceau, chemin);
            }

            if (bord.A > 0)
            {
                using var stylo = new Pen(bord, 1);
                g.DrawPath(stylo, chemin);
            }
        }

        if (Focused && ShowFocusCues)
        {
            using var chemin = Theme.Arrondi(RectangleF.Inflate(r, -2, -2), 6);
            using var stylo = new Pen(Color.FromArgb(150, Theme.Violet), 1.5f);
            g.DrawPath(stylo, chemin);
        }

        DessinerContenu(g, texte);
    }

    private void DessinerContenu(Graphics g, Color texte)
    {
        const TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix
                                  | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
        const int largeurIcone = 18;
        const int ecart = 7;

        var zone = new Rectangle(8, 0, Math.Max(0, Width - 16), Height);
        var tailleTexte = TextRenderer.MeasureText(g, Text, Font, new Size(int.MaxValue, Height), f);

        var avecIcone = !string.IsNullOrEmpty(_icone);
        if (avecIcone && tailleTexte.Width + largeurIcone + ecart > zone.Width) avecIcone = false;

        var total = tailleTexte.Width + (avecIcone ? largeurIcone + ecart : 0);
        var x = zone.X + Math.Max(0, (zone.Width - total) / 2);

        if (avecIcone)
        {
            TextRenderer.DrawText(g, _icone, Theme.Icone, new Rectangle(x, 0, largeurIcone, Height), texte,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x += largeurIcone + ecart;
        }

        TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Math.Max(0, zone.Right - x), Height), texte,
            f | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Tuile de télémétrie : pastille d'icône, libellé, valeur d'état.</summary>
internal sealed class TuileControl : Ctrl
{
    private string _valeur = "—";
    private Niveau _niveau = Niveau.Gris;

    public TuileControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    public string Titre { get; set; } = "";
    public string Icone { get; set; } = "";
    public bool MiseEnAvant { get; set; }

    public void Definir(string valeur, Niveau niveau)
    {
        if (_valeur == valeur && _niveau == niveau) return;
        _valeur = valeur;
        _niveau = niveau;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var accent = MiseEnAvant ? Theme.Violet : _niveau == Niveau.Gris ? Theme.Secondaire : Theme.Accent(_niveau);
        var fond = MiseEnAvant ? Theme.Teinte(Theme.Violet, 0.08f) : Theme.Carte;
        var bord = MiseEnAvant ? Theme.Melanger(fond, Theme.Violet, 0.35f) : Theme.Bordure;
        var glyphe = MiseEnAvant ? Theme.VioletTexte : _niveau == Niveau.Gris ? Theme.Secondaire : Theme.Texte(_niveau);

        var r = new RectangleF(0.5f, 0.5f, Width - 2f, Height - 2f);
        using (var chemin = Theme.Arrondi(r, 10))
        using (var pinceau = new SolidBrush(fond))
        using (var stylo = new Pen(bord, 1))
        {
            g.FillPath(pinceau, chemin);
            g.DrawPath(stylo, chemin);
        }

        var badge = new RectangleF(14, (Height - 36) / 2f, 36, 36);
        using (var chemin = Theme.Arrondi(badge, 9))
        using (var pinceau = new SolidBrush(Theme.Melanger(fond, accent, 0.16f)))
        {
            g.FillPath(pinceau, chemin);
        }

        TextRenderer.DrawText(g, Icone, Theme.Icone, Rectangle.Round(badge), glyphe,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var largeur = Math.Max(0, Width - 60 - 10);
        const TextFormatFlags base_ = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

        TextRenderer.DrawText(g, Titre, Theme.Petite, new Rectangle(60, 10, largeur, 16), Theme.Secondaire,
            base_ | TextFormatFlags.SingleLine);

        if (MiseEnAvant)
        {
            TextRenderer.DrawText(g, _valeur, Theme.Grand, new Rectangle(60, 28, largeur, 38), Theme.VioletTexte,
                base_ | TextFormatFlags.SingleLine);
        }
        else
        {
            var couleur = _niveau == Niveau.Gris ? Theme.Titre : Theme.Texte(_niveau);
            TextRenderer.DrawText(g, _valeur, Theme.Gras, new Rectangle(60, 28, largeur, Math.Max(0, Height - 28 - 8)), couleur,
                base_ | TextFormatFlags.WordBreak | TextFormatFlags.Top);
        }
    }
}