namespace CyberManager.Server.Setup;

/// <summary>Transforme les constats en messages humains : ce qui bloque, ce qu'il faut savoir, ce qui est rassurant.</summary>
internal static class Diagnostic
{
    public static void Evaluer(RapportDetection r)
    {
        var s = r.Systeme;
        var i = r.Installation;

        // ---- Bloquants -----------------------------------------------
        if (!s.Administrateur) r.Bloquants.Add("L'installation doit être lancée avec les droits d'administrateur.");
        if (!s.Os64Bits) r.Bloquants.Add("Cyber Manager serveur nécessite un Windows 64 bits.");
        if (s.Build < DetectionSysteme.BuildMinimum)
        {
            r.Bloquants.Add($"Windows est trop ancien (build {s.Build}) : Windows 10 (version 1607) ou plus récent est requis.");
        }

        Espace(r, s.LecteurCode, s.EspaceLibreCodeMo, "l'installation");
        if (!string.Equals(s.LecteurDonnees, s.LecteurCode, StringComparison.OrdinalIgnoreCase))
        {
            Espace(r, s.LecteurDonnees, s.EspaceLibreDonneesMo, "vos données");
        }

        if (i.Mode == ModeInstallation.Retrogradation) r.Bloquants.Add(i.Raison);

        // ---- Informations et avertissements --------------------------
        r.Infos.Add(i.Raison);

        if (i.DonneesExistantes)
        {
            r.Infos.Add("Des données existantes ont été trouvées : elles seront conservées telles quelles (base, réglages, sauvegardes).");
        }

        if (i.Mode != ModeInstallation.Nouvelle && i.SessionsEnCours is > 0)
        {
            r.Avertissements.Add($"{i.SessionsEnCours} session(s) payante(s) sont en cours : elles seront figées pendant l'opération. "
                                 + "Choisissez de préférence un moment calme.");
        }

        // ---- Ports ---------------------------------------------------
        var prefere = r.Ports.FirstOrDefault();
        if (prefere != null && prefere.Etat != EtatPort.Libre)
        {
            r.Avertissements.Add(prefere.Message + (r.PortRecommande is int p
                ? $" Le port {p} sera proposé à la place : les postes suivront grâce à la découverte automatique."
                : " Aucun port habituel n'est libre : le système en choisira un."));
        }
        else if (prefere != null)
        {
            r.Infos.Add(prefere.Message);
        }

        // ---- Réseau --------------------------------------------------
        var rs = r.Reseau;
        if (rs.NombreEligibles == 0)
        {
            r.Avertissements.Add("Aucune carte réseau connectée n'a été trouvée : les postes ne pourront pas joindre le serveur tant qu'un câble n'est pas branché.");
        }
        else
        {
            var choisie = rs.Interfaces.First(x => x.Recommandee);

            if (rs.NombreEligibles > 1)
            {
                r.Infos.Add($"Plusieurs cartes réseau détectées : « {choisie.Nom} » est présélectionnée ({choisie.Raison.ToLowerInvariant()}).");
            }

            if (choisie.Type == "wifi")
            {
                r.Avertissements.Add("Le serveur est relié en Wi-Fi : un câble réseau est plus fiable pour un serveur de cybercafé.");
            }

            if (choisie.Dhcp)
            {
                r.Avertissements.Add($"L'adresse de « {choisie.Nom} » est attribuée automatiquement (DHCP) : "
                                     + "une réservation d'adresse dans le routeur est recommandée (la découverte réseau compense un changement).");
            }
        }

        // ---- XAMPP ---------------------------------------------------
        var x = r.Xampp;
        if (x.Detecte)
        {
            var bases = x.Installations.SelectMany(a => a.BasesCyber).Distinct().ToList();
            r.Infos.Add(bases.Count > 0
                ? $"Un ancien XAMPP contenant {(bases.Count == 1 ? "la base" : "les bases")} « {string.Join("», « ", bases)} » a été trouvé : l'historique pourra être repris."
                : "Un ancien XAMPP a été trouvé, sans base Cyber Manager reconnue.");

            if (x.ApacheActif)
            {
                r.Avertissements.Add("XAMPP (Apache) est actif et occupe peut-être le port 80 : il ne sera pas modifié, un autre port peut être utilisé.");
            }
        }
    }

    private static void Espace(RapportDetection r, string lecteur, long? libre, string pour)
    {
        if (libre is not long mo) return;

        if (mo < DetectionSysteme.EspaceMinimumMo)
        {
            r.Bloquants.Add($"Espace disque insuffisant sur {lecteur} : {mo} Mo libres, {DetectionSysteme.EspaceMinimumMo} Mo nécessaires pour {pour}.");
        }
        else if (mo < DetectionSysteme.EspaceConfortMo)
        {
            r.Avertissements.Add($"Il reste peu d'espace sur {lecteur} ({mo} Mo) : pensez à libérer de la place.");
        }
    }
}