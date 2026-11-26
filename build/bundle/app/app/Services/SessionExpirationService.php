<?php

namespace App\Services;

/**
 * OBSOLÈTE : l'expiration des forfaits est assurée par PosteSessionService::expirerSiNecessaire()
 * (appelé par postes:maintenance et par le heartbeat), qui tient compte des temps de coupure.
 *
 * L'ancien code parcourait TOUTES les sessions « en cours » (Ethernet et Wi-Fi) et les clôturait à
 * date_heure_fin_prevue sans déduire les coupures, avec un état « expiree » inconnu du moteur actuel.
 * Il est neutralisé : la commande sessions:expire reste appelable mais ne touche plus aucune session.
 */
class SessionExpirationService
{
    public function verifierSessions(): int
    {
        return 0;
    }
}