using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace CyberManager.Server.Control;

/// <summary>Icônes dessinées en code (pastille colorée + « C ») : aucune ressource externe.</summary>
internal sealed class IconesEtat : IDisposable
{
    private readonly Dictionary<Niveau, Icon> _icones = new();

    public IconesEtat()
    {
        foreach (var n in Enum.GetValues<Niveau>())
        {
            _icones[n] = Creer(Presentation.Couleur(n));
        }
    }

    public Icon Pour(Niveau n) => _icones[n];

    public void Dispose()
    {
        foreach (var i in _icones.Values) i.Dispose();
        _icones.Clear();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private static Icon Creer(Color couleur)
    {
        using var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            using (var blanc = new SolidBrush(Color.White)) g.FillEllipse(blanc, 0, 0, 31, 31);
            using (var fond = new SolidBrush(couleur)) g.FillEllipse(fond, 3, 3, 25, 25);

            using var police = new Font("Segoe UI", 17f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("C", police, Brushes.White, new RectangleF(3, 3, 25, 26), format);
        }

        var handle = bmp.GetHicon();
        try
        {
            using var temporaire = Icon.FromHandle(handle);
            return (Icon)temporaire.Clone();   // copie indépendante du handle GDI
        }
        finally
        {
            DestroyIcon(handle);
        }
    }
}