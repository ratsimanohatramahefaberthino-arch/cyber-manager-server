using System.Net;
using System.Net.Sockets;
using CyberManager.Server.Setup;
using Xunit;

namespace CyberManager.Server.Setup.Tests;

public class ModeInstallationTests
{
    [Fact]
    public void Rien_d_installe_donne_nouvelle()
    {
        var d = DetectionInstallation.DeciderMode(null, "3.1.0", false);
        Assert.Equal(ModeInstallation.Nouvelle, d.Mode);
    }

    [Fact]
    public void Version_plus_ancienne_installee_donne_mise_a_jour()
    {
        var d = DetectionInstallation.DeciderMode("3.0.0", "3.1.0", true);
        Assert.Equal(ModeInstallation.MiseAJour, d.Mode);
        Assert.Contains("3.0.0", d.Raison);
        Assert.Contains("3.1.0", d.Raison);
    }

    [Fact]
    public void Meme_version_donne_reparation()
    {
        Assert.Equal(ModeInstallation.Reparation, DetectionInstallation.DeciderMode("3.1.0", "3.1.0", true).Mode);
    }

    [Fact]
    public void Versions_equivalentes_3_1_et_3_1_0_donnent_reparation()
    {
        Assert.Equal(ModeInstallation.Reparation, DetectionInstallation.DeciderMode("3.1", "3.1.0", true).Mode);
    }

    [Fact]
    public void Version_plus_recente_installee_refuse_le_retour_en_arriere()
    {
        Assert.Equal(ModeInstallation.Retrogradation, DetectionInstallation.DeciderMode("3.2.0", "3.1.0", true).Mode);
    }

    [Fact]
    public void Service_sans_code_donne_reparation()
    {
        Assert.Equal(ModeInstallation.Reparation, DetectionInstallation.DeciderMode(null, "3.1.0", true).Mode);
    }

    [Fact]
    public void Version_cible_invalide_leve_une_erreur()
    {
        Assert.Throws<ArgumentException>(() => DetectionInstallation.DeciderMode(null, "abc", false));
    }

    [Fact]
    public void Versions_avec_prefixe_et_suffixe_sont_acceptees()
    {
        Assert.True(VersionsApp.TryParse("v3.1.0-beta", out var v));
        Assert.Equal(new Version(3, 1, 0, 0), v);
    }
}

public class SondePortTests
{
    [Fact]
    public void Port_en_ecoute_est_occupe()
    {
        var ecoute = new TcpListener(IPAddress.Any, 0);
        ecoute.Start();

        try
        {
            var port = ((IPEndPoint)ecoute.LocalEndpoint).Port;
            var r = SondePort.Sonder(port);

            Assert.Equal(EtatPort.Occupe, r.Etat);
            Assert.Contains("déjà utilisé", r.Message);
        }
        finally
        {
            ecoute.Stop();
        }
    }

    [Fact]
    public void Port_ferme_est_libre()
    {
        var ecoute = new TcpListener(IPAddress.Any, 0);
        ecoute.Start();
        var port = ((IPEndPoint)ecoute.LocalEndpoint).Port;
        ecoute.Stop();

        Assert.Equal(EtatPort.Libre, SondePort.Sonder(port).Etat);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void Port_hors_limites_est_invalide(int port)
    {
        Assert.Equal(EtatPort.Invalide, SondePort.Sonder(port).Etat);
    }

    [Fact]
    public void Liste_de_ports_invalide_leve_une_erreur()
    {
        Assert.Throws<ArgumentException>(() => Commandes.AnalyserPorts("80,abc"));
    }
}

public class SortieTests
{
    [Fact]
    public void Ini_aplatit_sections_tableaux_et_booleens()
    {
        var r = new RapportDetection { VersionCible = "3.1.0", PortRecommande = 80 };
        r.Bloquants.Add("Premier problème");
        r.Systeme.Administrateur = true;
        r.Reseau.Interfaces.Add(new InterfaceReseau
        {
            Nom = "Ethernet",
            Recommandee = true,
            Adresses = { new AdresseIp { Ip = "192.168.1.10", Prefixe = 24 } }
        });

        var ini = Sortie.Ini(r);

        Assert.Contains("[meta]", ini);
        Assert.Contains("version_cible=3.1.0", ini);
        Assert.Contains("port_recommande=80", ini);
        Assert.Contains("[bloquants]", ini);
        Assert.Contains("count=1", ini);
        Assert.Contains("1=Premier problème", ini);
        Assert.Contains("administrateur=1", ini);
        Assert.Contains("interfaces_1_nom=Ethernet", ini);
        Assert.Contains("interfaces_1_adresses_1_ip=192.168.1.10", ini);
        Assert.Contains("interfaces_1_recommandee=1", ini);
    }

    [Fact]
    public void Json_utilise_snake_case_et_enums_lisibles()
    {
        var json = Sortie.Json(new RapportPorts
        {
            Ports = { new ResultatPort { Port = 80, Etat = EtatPort.Occupe } }
        });

        Assert.Contains("\"port_recommande\"", json);
        Assert.Contains("\"etat\": \"occupe\"", json);
    }
}