namespace CyberManager.Server.Setup;

/// <summary>
/// « Empêcher la mise en veille / l'hibernation de ce PC » (§2.5.3). Sans veille, le serveur ne s'endort pas en pleine journée.
/// Modifie le mode d'alimentation actif ; la désinstallation ne restaure pas les anciennes valeurs (à documenter pour le gérant).
/// </summary>
internal static class EtapeVeille
{
    public static EtapeResultat Executer(Demande d)
    {
        if (!d.EmpecherVeille)
        {
            return EtapeResultat.Ignoree("Non demandé : les réglages de veille de Windows n'ont pas été modifiés.");
        }

        var powercfg = Path.Combine(Environment.SystemDirectory, "powercfg.exe");
        var echecsSecteur = new List<string>();
        var etape = EtapeResultat.Reussie("Mise en veille et hibernation désactivées (secteur et batterie).");

        foreach (var reglage in new[] { "standby-timeout-ac", "hibernate-timeout-ac", "standby-timeout-dc", "hibernate-timeout-dc" })
        {
            var r = Execution.Lancer(powercfg, new[] { "/change", reglage, "0" }, TimeSpan.FromSeconds(20));
            if (r.Ok) continue;

            if (reglage.EndsWith("-ac", StringComparison.Ordinal)) echecsSecteur.Add(reglage);
            else etape.Alertes.Add($"Réglage « {reglage} » non appliqué (poste sans batterie).");
        }

        return echecsSecteur.Count > 0
            ? EtapeResultat.Echouee("Réglage de la veille impossible : " + string.Join(", ", echecsSecteur))
            : etape;
    }
}