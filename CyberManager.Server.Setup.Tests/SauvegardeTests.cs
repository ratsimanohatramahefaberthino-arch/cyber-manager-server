using CyberManager.Server.Common;
using CyberManager.Server.Setup;
using Xunit;

namespace CyberManager.Server.Setup.Tests;

public class SauvegardeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"\\serveur\partage\sauvegardes")]
    [InlineData(@"relatif\dossier")]
    [InlineData(@"E:")]
    [InlineData(@"E:\")]
    [InlineData(@"E:\a\..\b")]
    public void Chemins_refuses(string? chemin)
    {
        Assert.NotNull(EtapeSauvegarde.ValiderChemin(chemin));
    }

    [Theory]
    [InlineData(@"E:\Sauvegardes")]
    [InlineData(@"D:\Mon cyber\Sauvegardes é")]
    [InlineData("F:/Cyber/Sauvegardes")]
    public void Chemins_acceptes(string chemin)
    {
        Assert.Null(EtapeSauvegarde.ValiderChemin(chemin));
    }

    [Fact]
    public void Dossier_dans_le_code_est_refuse()
    {
        Assert.NotNull(EtapeSauvegarde.ValiderChemin(Path.Combine(Chemins.Code, "sauvegardes")));
    }

    [Fact]
    public void Dossier_dans_windows_est_refuse()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.NotNull(EtapeSauvegarde.ValiderChemin(Path.Combine(windows, "Temp", "sauv")));
    }

    [Fact]
    public void L_etape_sauvegarde_se_place_apres_base_et_avant_service()
    {
        var e = MiseEnPlace.AnalyserEtapes("service,sauvegarde,base");
        Assert.Equal(new[] { "base", "sauvegarde", "service" }, e);
    }

    [Fact]
    public void Sans_dossier_l_etape_est_ignoree()
    {
        var r = EtapeSauvegarde.Executer(new Demande());
        Assert.Equal(Statut.Ignore, r.Statut);
    }

    [Fact]
    public void Un_chemin_invalide_fait_echouer_l_etape()
    {
        var r = EtapeSauvegarde.Executer(new Demande { DossierSauvegarde = @"\\serveur\partage" });
        Assert.Equal(Statut.Echec, r.Statut);
    }
}