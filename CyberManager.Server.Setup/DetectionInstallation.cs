using System.ServiceProcess;
using CyberManager.Server.Common;
using Microsoft.Data.Sqlite;

namespace CyberManager.Server.Setup;

internal readonly record struct DecisionMode(ModeInstallation Mode, string Raison);

internal static class VersionsApp
{
    /// <summary>Accepte « 3.1 », « 3.1.0 », « v3.1.0-beta » ; normalise pour que 3.1 == 3.1.0.</summary>
    public static bool TryParse(string? texte, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(texte)) return false;

        var t = texte.Trim().TrimStart('v', 'V');
        var i = t.IndexOfAny(new[] { '-', '+', ' ' });
        if (i > 0) t = t[..i];

        if (!Version.TryParse(t, out var p)) return false;

        version = new Version(p.Major, p.Minor, Math.Max(p.Build, 0), Math.Max(p.Revision, 0));
        return true;
    }
}

internal static class DetectionInstallation
{
    public static InfoInstallation Detecter(string versionCible)
    {
        var (existe, etat, automatique) = LireService();
        var versionCode = Chemins.LireVersionApplication();
        var basePresente = File.Exists(Chemins.BaseSqlite);
        var envPresent = File.Exists(Chemins.EnvLaravel);
        var decision = DeciderMode(versionCode, versionCible, existe);

        return new InfoInstallation
        {
            Mode = decision.Mode,
            Raison = decision.Raison,
            VersionCible = versionCible,
            VersionCode = versionCode,
            VersionMigree = LireTexte(Chemins.VersionInstallee),
            ServiceExiste = existe,
            ServiceEtat = etat,
            ServiceAutomatique = automatique,
            CodePresent = versionCode != null,
            DonneesExistantes = basePresente || envPresent,
            BasePresente = basePresente,
            EnvPresent = envPresent,
            SessionsEnCours = basePresente ? CompterSessionsEnCours() : null
        };
    }

    /// <summary>Règle unique : nouvelle / mise à jour / réparation / retour en arrière refusé. Les données ne changent pas le mode.</summary>
    public static DecisionMode DeciderMode(string? installee, string cible, bool serviceExiste)
    {
        if (!VersionsApp.TryParse(cible, out var vc))
        {
            throw new ArgumentException($"Version cible invalide : « {cible} ».");
        }

        if (!VersionsApp.TryParse(installee, out var vi))
        {
            return serviceExiste
                ? new DecisionMode(ModeInstallation.Reparation,
                    "Une installation incomplète a été trouvée : elle sera réparée, vos données sont conservées.")
                : new DecisionMode(ModeInstallation.Nouvelle,
                    "Première installation de Cyber Manager sur cet ordinateur.");
        }

        var c = vc.CompareTo(vi);

        if (c > 0)
        {
            return new DecisionMode(ModeInstallation.MiseAJour,
                $"Mise à jour de la version {installee} vers la version {cible} : vos données sont conservées.");
        }

        if (c == 0)
        {
            return new DecisionMode(ModeInstallation.Reparation,
                $"La version {cible} est déjà installée : elle sera réparée, vos données sont conservées.");
        }

        return new DecisionMode(ModeInstallation.Retrogradation,
            $"La version {installee} est déjà installée et elle est plus récente que celle de cet installateur ({cible}) : "
            + "le retour en arrière est refusé pour protéger vos données.");
    }

    private static (bool Existe, string? Etat, bool Automatique) LireService()
    {
        try
        {
            foreach (var s in ServiceController.GetServices())
            {
                using (s)
                {
                    if (s.ServiceName.Equals(Constantes.NomService, StringComparison.OrdinalIgnoreCase))
                    {
                        return (true, s.Status.ToString(), s.StartType == ServiceStartMode.Automatic);
                    }
                }
            }
        }
        catch
        {
            // liste des services illisible : on considère le service comme absent
        }

        return (false, null, false);
    }

    private static string? LireTexte(string chemin)
    {
        try
        {
            if (!File.Exists(chemin)) return null;
            var t = File.ReadAllText(chemin).Trim();
            return t.Length > 0 ? t : null;
        }
        catch
        {
            return null;
        }
    }

    private static int? CompterSessionsEnCours()
    {
        try
        {
            var chaine = new SqliteConnectionStringBuilder
            {
                DataSource = Chemins.BaseSqlite,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString();

            using var c = new SqliteConnection(chaine);
            c.Open();

            using (var attente = c.CreateCommand())
            {
                attente.CommandText = "PRAGMA busy_timeout = 3000;";
                attente.ExecuteNonQuery();
            }

            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM cyber_sessions WHERE etat = 'en_cours' AND poste_id IS NOT NULL;";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch
        {
            return null;
        }
    }
}