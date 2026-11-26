using System.Text;
using CyberManager.Server.Setup;
using Xunit;

namespace CyberManager.Server.Setup.Tests;

public class SidServiceVirtuelTests
{
    [Fact]
    public void Calcul_conforme_au_sid_connu_de_TrustedInstaller()
    {
        // Vecteur de test public : le SID de « NT SERVICE\TrustedInstaller »
        Assert.Equal(
            "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464",
            SidServiceVirtuel.Pour("TrustedInstaller").Value);
    }

    [Fact]
    public void Le_nom_est_insensible_a_la_casse()
    {
        Assert.Equal(SidServiceVirtuel.Pour("CyberManagerServer"), SidServiceVirtuel.Pour("cybermanagerserver"));
    }
}

public class EtapesTests
{
    [Fact]
    public void Toutes_donne_l_ordre_sur_sans_arreter_ni_demarrer()
    {
        var e = MiseEnPlace.AnalyserEtapes("toutes");
        Assert.Equal(new[] { "dossiers", "configuration", "taches", "parefeu", "base", "service", "veille" }, e);
    }

    [Fact]
    public void La_saisie_est_remise_dans_l_ordre_sur()
    {
        var e = MiseEnPlace.AnalyserEtapes("service, base,dossiers");
        Assert.Equal(new[] { "dossiers", "base", "service" }, e);
    }

    [Fact]
    public void Etape_inconnue_leve_une_erreur()
    {
        Assert.Throws<ArgumentException>(() => MiseEnPlace.AnalyserEtapes("dossiers,magie"));
    }
}

public class DemandeTests
{
    private static Demande Valide() => new()
    {
        NomCyber = "Cyber Étoile",
        AdminNom = "Rakoto Jean",
        AdminEmail = "gerant@cyber.mg",
        MotDePasse = "Motdepasse2026",
        PortHttp = 80
    };

    [Fact]
    public void Demande_complete_est_valide() => Assert.Empty(Valide().Valider());

    [Fact]
    public void Administrateur_partiel_est_refuse()
    {
        var d = Valide();
        d.MotDePasse = null;
        Assert.Contains(d.Valider(), m => m.Contains("incomplètes"));
    }

    [Theory]
    [InlineData("pas-un-email")]
    [InlineData("a@b")]
    [InlineData("a@.mg")]
    public void Email_invalide_est_refuse(string email)
    {
        var d = Valide();
        d.AdminEmail = email;
        Assert.Contains(d.Valider(), m => m.Contains("e-mail"));
    }

    [Fact]
    public void Mot_de_passe_court_est_refuse()
    {
        var d = Valide();
        d.MotDePasse = "court";
        Assert.Contains(d.Valider(), m => m.Contains("mot de passe"));
    }

    [Fact]
    public void Port_hors_limites_est_refuse()
    {
        var d = Valide();
        d.PortHttp = 70000;
        Assert.Contains(d.Valider(), m => m.Contains("port"));
    }

    [Fact]
    public void Sans_administrateur_la_demande_reste_valide()
    {
        Assert.Empty(new Demande { PortHttp = 8080 }.Valider());
    }

    [Fact]
    public void Charger_lit_le_json_snake_case_puis_efface_le_fichier()
    {
        var chemin = Path.Combine(Path.GetTempPath(), "cm-demande-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(chemin,
            "{\"mode\":\"reparation\",\"nom_cyber\":\"Cyber Étoile\",\"admin_email\":\"a@cyber.mg\",\"mot_de_passe\":\"Secret1234\",\"port_http\":8080,\"empecher_veille\":true}",
            new UTF8Encoding(false));

        var d = Demande.Charger(chemin, effacer: true);

        Assert.Equal("reparation", d.Mode);
        Assert.Equal("Cyber Étoile", d.NomCyber);
        Assert.Equal("Secret1234", d.MotDePasse);
        Assert.Equal(8080, d.PortHttp);
        Assert.True(d.EmpecherVeille);
        Assert.False(File.Exists(chemin));
    }

    [Fact]
    public void Charger_un_json_invalide_leve_une_erreur_claire()
    {
        var chemin = Path.Combine(Path.GetTempPath(), "cm-demande-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(chemin, "{pas du json");

        Assert.Throws<ArgumentException>(() => Demande.Charger(chemin, effacer: true));
        Assert.False(File.Exists(chemin));
    }
}

public class ConfigurationTests
{
    [Fact]
    public void Cle_app_est_au_format_laravel_et_unique()
    {
        var a = EtapeConfiguration.GenererCleApp();
        var b = EtapeConfiguration.GenererCleApp();

        Assert.StartsWith("base64:", a);
        Assert.Equal(32, Convert.FromBase64String(a["base64:".Length..]).Length);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Env_de_production_est_complet_et_sans_secret_utilisateur()
    {
        var env = EtapeConfiguration.ContenuEnv("base64:CLE", @"C:\ProgramData\CyberManager\data\cyber.sqlite");

        Assert.Contains("APP_ENV=production", env);
        Assert.Contains("APP_DEBUG=false", env);
        Assert.Contains("APP_KEY=base64:CLE", env);
        Assert.Contains("DB_DATABASE=\"C:/ProgramData/CyberManager/data/cyber.sqlite\"", env);
        Assert.Contains("LOG_CHANNEL=daily", env);
        Assert.DoesNotContain("PASSWORD", env, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Un_fichier_existant_n_est_jamais_reecrit()
    {
        var dossier = Path.Combine(Path.GetTempPath(), "cm-cfg-" + Guid.NewGuid().ToString("N"));
        var fichier = Path.Combine(dossier, "config", ".env");

        try
        {
            Assert.True(EtapeConfiguration.EcrireSiAbsent(fichier, "PREMIER"));
            Assert.False(EtapeConfiguration.EcrireSiAbsent(fichier, "SECOND"));
            Assert.Equal("PREMIER", File.ReadAllText(fichier));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(fichier)!, "*.tmp-*"));
        }
        finally
        {
            if (Directory.Exists(dossier)) Directory.Delete(dossier, true);
        }
    }
}

public class JsonDernierObjetTests
{
    [Fact]
    public void Prend_le_dernier_objet_json_apres_du_bruit()
    {
        var sortie = "Warning: quelque chose\n{\"ok\":false}\nautre ligne\n{\"ok\":true,\"base\":\"creee\",\"admin\":\"cree\"}\n";
        var b = JsonDernierObjet.Lire<BilanInstaller>(sortie);

        Assert.NotNull(b);
        Assert.True(b!.Ok);
        Assert.Equal("creee", b.Base);
    }

    [Fact]
    public void Sans_json_renvoie_null()
    {
        Assert.Null(JsonDernierObjet.Lire<BilanInstaller>("rien d'utile"));
    }
}