using System.Drawing.Drawing2D;
using CyberManager.Server.Common.Ipc;
using Ctrl = System.Windows.Forms.Control;

namespace CyberManager.Server.Control;

/// <summary>Grand voyant rond avec halo pulsant et glyphe d'état.</summary>
internal sealed class VoyantControl : Ctrl
{
    private Niveau _niveau = Niveau.Gris;
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 50 };
    private float _phase;

    public VoyantControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;

        _animation.Tick += (_, _) =>
        {
            if (!Visible || _niveau is Niveau.Gris or Niveau.Rouge) return;

            _phase += _niveau == Niveau.Orange ? 0.22f : 0.11f;
            if (_phase > MathF.Tau) _phase -= MathF.Tau;
            Invalidate();
        };
        _animation.Start();
    }

    public Niveau Niveau
    {
        get => _niveau;
        set
        {
            if (_niveau == value) return;
            _niveau = value;
            Invalidate();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _animation.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var d = Math.Min(Width, Height) - 28;
        if (d < 16) return;

        var x = (Width - d) / 2f;
        var y = (Height - d) / 2f;
        var couleur = Theme.Accent(_niveau);

        var anime = _niveau is Niveau.Vert or Niveau.Orange;
        var k = anime ? (MathF.Sin(_phase) + 1f) / 2f : 0f;

        // Halo extérieur pulsant
        var grand = 6f + 8f * k;
        var alpha = anime ? (int)(70 - 45 * k) : 50;
        using (var halo = new SolidBrush(Color.FromArgb(alpha, couleur)))
        {
            g.FillEllipse(halo, x - grand, y - grand, d + 2 * grand, d + 2 * grand);
        }

        using (var halo2 = new SolidBrush(Color.FromArgb(60, couleur)))
        {
            g.FillEllipse(halo2, x - 4, y - 4, d + 8, d + 8);
        }

        using (var degrade = new LinearGradientBrush(new RectangleF(x, y, d, d),
                   Theme.Melanger(couleur, Color.White, 0.25f), couleur, 90f))
        {
            g.FillEllipse(degrade, x, y, d, d);
        }

        var glyphe = _niveau switch
        {
            Niveau.Vert => Glyphes.Coche,
            Niveau.Orange => Glyphes.Avertissement,
            Niveau.Rouge => Glyphes.Erreur,
            _ => Glyphes.Points
        };

        using var police = new Font("Segoe MDL2 Assets", d * 0.42f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(glyphe, police, Brushes.White, new RectangleF(x, y, d, d), format);
    }
}