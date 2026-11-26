using System.Security.Principal;
using CyberManager.Server.Common;
using Microsoft.Win32;

namespace CyberManager.Server.Setup;

internal static class DetectionSysteme
{
    /// <summary>Windows 10 version 1607 : minimum de .NET 8.</summary>
    public const int BuildMinimum = 14393;

    public const long EspaceMinimumMo = 1024;
    public const long EspaceConfortMo = 3072;

    public static InfoSysteme Detecter()
    {
        var build = Environment.OSVersion.Version.Build;
        var racineCode = Path.GetPathRoot(Chemins.Code) ?? "";
        var racineDonnees = Path.GetPathRoot(Chemins.Donnees) ?? "";

        return new InfoSysteme
        {
            NomWindows = LireNomWindows(build),
            Build = build,
            Os64Bits = Environment.Is64BitOperatingSystem,
            Administrateur = EstAdministrateur(),
            Supporte = build >= BuildMinimum && Environment.Is64BitOperatingSystem,
            NomMachine = Environment.MachineName,
            LecteurCode = racineCode,
            EspaceLibreCodeMo = EspaceLibre(racineCode),
            LecteurDonnees = racineDonnees,
            EspaceLibreDonneesMo = EspaceLibre(racineDonnees)
        };
    }

    private static bool EstAdministrateur()
    {
        try
        {
            using var identite = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identite).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static string LireNomWindows(int build)
    {
        try
        {
            using var cle = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var nom = cle?.GetValue("ProductName") as string ?? "Windows";

            // Windows 11 conserve « Windows 10 » dans le registre
            if (build >= 22000) nom = nom.Replace("Windows 10", "Windows 11");
            return nom;
        }
        catch
        {
            return "Windows";
        }
    }

    private static long? EspaceLibre(string racine)
    {
        try
        {
            if (string.IsNullOrEmpty(racine)) return null;
            return new DriveInfo(racine).AvailableFreeSpace / (1024 * 1024);
        }
        catch
        {
            return null;
        }
    }
}