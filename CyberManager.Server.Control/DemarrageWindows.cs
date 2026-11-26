using Microsoft.Win32;

namespace CyberManager.Server.Control;

/// <summary>Case « Lancer avec Windows » : valeur Run de l'utilisateur (lancement silencieux dans la zone de notification).</summary>
internal static class DemarrageWindows
{
    private const string CleRun = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string NomValeur = "CyberManagerServer";

    public static bool EstActif()
    {
        return Present(Registry.CurrentUser) || Present(Registry.LocalMachine);
    }

    private static bool Present(RegistryKey racine)
    {
        try
        {
            using var cle = racine.OpenSubKey(CleRun);
            return cle?.GetValue(NomValeur) is string s && s.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Definir(bool actif)
    {
        if (actif)
        {
            using var cle = Registry.CurrentUser.CreateSubKey(CleRun);
            cle.SetValue(NomValeur, $"\"{Environment.ProcessPath}\" --minimise");
            return;
        }

        foreach (var racine in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var cle = racine.OpenSubKey(CleRun, true);
                cle?.DeleteValue(NomValeur, false);
            }
            catch
            {
                // HKLM : droits insuffisants pour un utilisateur standard
            }
        }
    }
}