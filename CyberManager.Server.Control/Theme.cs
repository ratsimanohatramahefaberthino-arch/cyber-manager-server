using System.Drawing.Drawing2D;
using Microsoft.Win32;
using CyberManager.Server.Common.Ipc;

namespace CyberManager.Server.Control;

/// <summary>Glyphes Segoe MDL2 Assets (présente sur Windows 10 et 11).</summary>
internal static class Glyphes
{
    public const string Lecture = "\uE768";
    public const string Arret = "\uE71A";
    public const string Redemarrer = "\uE72C";
    public const string Sauvegarde = "\uE74E";
    public const string Cle = "\uE192";
    public const string Diagnostic = "\uE9D9";
    public const string Ouvrir = "\uE8A7";
    public const string Copier = "\uE8C8";
    public const string Coche = "\uE73E";
    public const string Avertissement = "\uE7BA";
    public const string Erreur = "\uE711";
    public const string Points = "\uE712";
    public const string Info = "\uE946";
    public const string Reduire = "\uE921";

    // Tuiles de télémétrie
    public const string Web = "\uE774";
    public const string Php = "\uE713";
    public const string Planificateur = "\uE787";
    public const string Base = "\uE8F1";
    public const string Decouverte = "\uE701";
    public const string Sessions = "\uE77B";
    public const string Disque = "\uE958";
    public const string Duree = "\uE121";
}

/// <summary>Palette, polices et utilitaires graphiques. Suit le thème Windows.</summary>
internal static class Theme
{
    public static bool Sombre { get; }

    static Theme()
    {
        try
        {
            using var cle = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            Sombre = cle?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            Sombre = false;
        }
    }

    private static Color H(int rgb) => Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);

    // Surfaces
    public static Color Fond => Sombre ? H(0x0F172A) : H(0xF8FAFC);
    public static Color Carte => Sombre ? H(0x1E293B) : Color.White;
    public static Color Bordure => Sombre ? H(0x334155) : H(0xE2E8F0);
    public static Color Survol => Sombre ? H(0x334155) : H(0xF1F5F9);

    // Textes
    public static Color Titre => Sombre ? H(0xF1F5F9) : H(0x1E293B);
    public static Color Secondaire => Sombre ? H(0x94A3B8) : H(0x64748B);
    public static Color TexteDesactive => Sombre ? H(0x64748B) : H(0x94A3B8);

    // Marque et statuts
    public static Color Violet => H(0x7C3AED);
    public static Color VioletSurvol => H(0x6D28D9);
    public static Color VioletTexte => Sombre ? H(0xA78BFA) : H(0x6D28D9);
    public static Color Emeraude => H(0x10B981);
    public static Color EmeraudeFonce => H(0x059669);
    public static Color Ambre => H(0xF59E0B);
    public static Color Rouge => H(0xEF4444);

    public static Color Accent(Niveau n) => n switch
    {
        Niveau.Vert => Emeraude,
        Niveau.Orange => Ambre,
        Niveau.Rouge => Rouge,
        _ => H(0x94A3B8)
    };

    public static Color Texte(Niveau n) => Sombre
        ? n switch { Niveau.Vert => H(0x34D399), Niveau.Orange => H(0xFBBF24), Niveau.Rouge => H(0xF87171), _ => H(0x94A3B8) }
        : n switch { Niveau.Vert => H(0x047857), Niveau.Orange => H(0xB45309), Niveau.Rouge => H(0xB91C1C), _ => H(0x475569) };

    public static Color Melanger(Color bas, Color haut, float a) => Color.FromArgb(
        255,
        (int)(bas.R + (haut.R - bas.R) * a),
        (int)(bas.G + (haut.G - bas.G) * a),
        (int)(bas.B + (haut.B - bas.B) * a));

    public static Color Teinte(Color c, float a = 0.12f) => Melanger(Carte, c, a);

    // Polices
    public static readonly Font Normale = new("Segoe UI", 9.5f);
    public static readonly Font Gras = new("Segoe UI Semibold", 9.5f);
    public static readonly Font Compacte = new("Segoe UI Semibold", 8.8f);
    public static readonly Font Petite = new("Segoe UI", 8.5f);
    public static readonly Font PetiteGras = new("Segoe UI Semibold", 7.5f);
    public static readonly Font TitreCarte = new("Segoe UI Semibold", 11f);
    public static readonly Font TitreHero = new("Segoe UI Semibold", 17f);
    public static readonly Font Grand = new("Segoe UI Semibold", 20f);
    public static readonly Font Code = new("Consolas", 10.5f);
    public static readonly Font Icone = new("Segoe MDL2 Assets", 11f);
    public static readonly Font IconeGrande = new("Segoe MDL2 Assets", 18f);

    public static GraphicsPath Arrondi(RectangleF r, float rayon)
    {
        var d = rayon * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}