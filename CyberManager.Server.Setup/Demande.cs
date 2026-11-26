using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace CyberManager.Server.Setup;

/// <summary>
/// Demande de mise en place, écrite par l'assistant dans un fichier JSON UTF-8 (snake_case).
/// Le fichier est lu puis écrasé et supprimé immédiatement (le mot de passe n'y reste pas).
/// </summary>
public sealed class Demande
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public int V { get; set; } = 1;

    /// <summary>X.Y.Z ; par défaut le fichier VERSION du code installé.</summary>
    public string? VersionCible { get; set; }

    /// <summary>nouvelle, mise_a_jour, reparation, ou auto. À décider AVANT la copie des fichiers (voir MiseEnPlace.ResoudreMode).</summary>
    public string? Mode { get; set; } = "auto";

    public string? NomCyber { get; set; }
    public string? AdminNom { get; set; }
    public string? AdminEmail { get; set; }
    public string? MotDePasse { get; set; }
    public int? PortHttp { get; set; }

    /// <summary>Carte réseau choisie (information : Caddy écoute sur toutes les cartes).</summary>
    public string? Interface { get; set; }

    public bool EmpecherVeille { get; set; }

    /// <summary>Dossier de sauvegarde choisi (lecteur local, ex. E:\Sauvegardes). Vide : dossier par défaut.</summary>
    public string? DossierSauvegarde { get; set; }

    public bool AdminFourni =>
        !string.IsNullOrEmpty(AdminNom) && !string.IsNullOrEmpty(AdminEmail) && !string.IsNullOrEmpty(MotDePasse);

    public List<string> Valider()
    {
        var e = new List<string>();

        if (NomCyber != null && (NomCyber.Trim().Length == 0 || NomCyber.Trim().Length > 80))
        {
            e.Add("Le nom du cyber doit comporter de 1 à 80 caractères.");
        }

        var presents = new[] { AdminNom, AdminEmail, MotDePasse }.Count(s => !string.IsNullOrEmpty(s));

        if (presents is > 0 and < 3)
        {
            e.Add("Les informations de l'administrateur sont incomplètes (nom, e-mail et mot de passe sont tous nécessaires).");
        }
        else if (presents == 3)
        {
            var nom = AdminNom!.Trim();
            if (nom.Length is < 2 or > 120) e.Add("Le nom de l'administrateur doit comporter de 2 à 120 caractères.");

            if (!EmailValide(AdminEmail!)) e.Add("L'adresse e-mail de l'administrateur n'est pas valide.");

            if (MotDePasse!.Length is < 8 or > 128) e.Add("Le mot de passe doit comporter de 8 à 128 caractères.");
        }

        if (PortHttp is int p && (p < 1 || p > 65535))
        {
            e.Add("Le port HTTP doit être un entier entre 1 et 65535.");
        }

        return e;
    }

    internal static bool EmailValide(string texte)
    {
        var t = texte.Trim();
        if (t.Length > 190 || !MailAddress.TryCreate(t, out var m) || m.Address != t) return false;

        var domaine = t[(t.IndexOf('@') + 1)..];
        return domaine.Contains('.') && !domaine.StartsWith('.') && !domaine.EndsWith('.');
    }

    public static Demande Charger(string chemin, bool effacer)
    {
        string texte;

        try
        {
            texte = File.ReadAllText(chemin, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Fichier de demande illisible : {ex.Message}");
        }
        finally
        {
            if (effacer) Effacer(chemin);
        }

        try
        {
            return JsonSerializer.Deserialize<Demande>(texte.TrimStart('\uFEFF'), Options)
                   ?? throw new ArgumentException("La demande est vide.");
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("La demande n'est pas un JSON valide : " + ex.Message);
        }
    }

    /// <summary>Meilleur effort : écrase le contenu puis supprime (sans garantie sur un SSD).</summary>
    private static void Effacer(string chemin)
    {
        try { File.WriteAllBytes(chemin, new byte[new FileInfo(chemin).Length]); } catch { /* verrouillé ou absent */ }
        try { File.Delete(chemin); } catch { /* idem */ }
    }
}