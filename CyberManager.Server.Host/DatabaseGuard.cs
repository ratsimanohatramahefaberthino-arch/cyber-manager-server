using CyberManager.Server.Common;
using Microsoft.Data.Sqlite;

namespace CyberManager.Server.Host;

internal sealed record ResultatBase(bool Ok, bool Existe, string Detail);

/// <summary>Contrôle de la base SQLite au démarrage : PRAGMA quick_check. Jamais de démarrage silencieux sur base douteuse.</summary>
internal sealed class DatabaseGuard
{
    private static SqliteConnection Ouvrir(string chemin)
    {
        var chaine = new SqliteConnectionStringBuilder
        {
            DataSource = chemin,
            Mode = SqliteOpenMode.ReadWrite,   // ReadWrite : permet à SQLite de rejouer le WAL après une coupure
            Pooling = false                    // aucun handle gardé ouvert sur cyber.sqlite
        }.ToString();

        var c = new SqliteConnection(chaine);
        c.Open();
        return c;
    }

    public ResultatBase Controler()
    {
        var chemin = Chemins.BaseSqlite;

        if (!File.Exists(chemin))
        {
            return new ResultatBase(true, false, "Base absente : première installation.");
        }

        try
        {
            using var c = Ouvrir(chemin);

            using (var attente = c.CreateCommand())
            {
                attente.CommandText = "PRAGMA busy_timeout = 8000;";
                attente.ExecuteNonQuery();
            }

            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA quick_check;";

            var lignes = new List<string>();
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read() && lignes.Count < 5)
                {
                    lignes.Add(Convert.ToString(r.GetValue(0)) ?? "");
                }
            }

            var ok = lignes.Count == 1 && lignes[0].Equals("ok", StringComparison.OrdinalIgnoreCase);
            return new ResultatBase(ok, true, ok ? "quick_check : ok" : string.Join(" | ", lignes));
        }
        catch (Exception ex)
        {
            return new ResultatBase(false, true, ex.Message);
        }
    }

    /// <summary>Nombre de sessions de poste « en_cours » ; null si la table est absente ou illisible.</summary>
    public int? CompterSessionsEnCours()
    {
        var chemin = Chemins.BaseSqlite;
        if (!File.Exists(chemin)) return null;

        try
        {
            using var c = Ouvrir(chemin);

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