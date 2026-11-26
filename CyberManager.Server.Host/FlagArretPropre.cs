using System.Text;
using CyberManager.Server.Common;

namespace CyberManager.Server.Host;

/// <summary>
/// etat\arret_propre.flag : « true » = arrêt contrôlé ; « false » = serveur en marche (ou coupure si on le retrouve ainsi).
/// Écriture durable (WriteThrough + Flush sur disque + remplacement atomique) : c'est la preuve qui survit à la coupure.
/// </summary>
internal static class FlagArretPropre
{
    /// <summary>null si le fichier est absent ou illisible (première installation).</summary>
    public static bool? Lire()
    {
        try
        {
            if (!File.Exists(Chemins.FlagArretPropre)) return null;

            var t = File.ReadAllText(Chemins.FlagArretPropre).Trim().ToLowerInvariant();
            return t is "true" or "1";
        }
        catch
        {
            return null;
        }
    }

    public static void Ecrire(bool propre)
    {
        Directory.CreateDirectory(Chemins.Etat);

        var temporaire = Chemins.FlagArretPropre + ".tmp";
        var octets = new UTF8Encoding(false).GetBytes(propre ? "true" : "false");

        using (var fs = new FileStream(temporaire, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            fs.Write(octets, 0, octets.Length);
            fs.Flush(true);
        }

        File.Move(temporaire, Chemins.FlagArretPropre, true);
    }
}