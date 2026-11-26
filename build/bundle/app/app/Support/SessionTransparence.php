<?php

namespace App\Support;

use App\Models\Session;
use Carbon\Carbon;

/**
 * Distingue une pause manuelle d'une session conservée après coupure,
 * et calcule la ligne « Temps non facturé : coupure ».
 */
final class SessionTransparence
{
    public const ETAT_SUSPENDUE = 'en_pause';   // ADAPTER : valeur réelle de cyber_sessions.etat

    public static function estSuspendue(Session $s): bool
    {
        return $s->etat === self::ETAT_SUSPENDUE;
    }

    /** @return array{type:?string, libelle:?string, non_facture_s:?int, incident_id:?int} */
    public static function pour(Session $s): array
    {
        $coupure = in_array($s->motif_pause, [Session::PAUSE_COUPURE_SERVEUR, Session::PAUSE_COUPURE_POSTE], true);

        $type = null;
        $libelle = null;
        if (self::estSuspendue($s)) {
            if ($s->motif_pause === Session::PAUSE_COUPURE_SERVEUR) {
                $type = 'coupure_serveur'; $libelle = 'Conservée — coupure de courant (serveur)';
            } elseif ($s->motif_pause === Session::PAUSE_COUPURE_POSTE) {
                $type = 'coupure_poste';   $libelle = 'Conservée — coupure du poste';
            } else {
                $type = 'manuelle';        $libelle = 'Mise en pause manuellement';
            }
        }

        $nonFacture = null;
        if (($s->duree_coupure_secondes ?? 0) > 0) {
            $nonFacture = (int) $s->duree_coupure_secondes;                      // session déjà reprise
        } elseif ($coupure && $s->date_heure_suspension) {                       // coupure encore en cours
            $fin = $s->poste_revenu_at ? Carbon::parse($s->poste_revenu_at) : now();
            $nonFacture = max(0, $fin->getTimestamp() - Carbon::parse($s->date_heure_suspension)->getTimestamp());
        }

        return [
            'type'          => $type,
            'libelle'       => $libelle,
            'non_facture_s' => $nonFacture,
            'incident_id'   => $s->incident_id,
        ];
    }

    /** Temps de consommation conservé au moment du gel. ADAPTER si Session a déjà un accesseur plus fin (pauses manuelles). */
    public static function tempsConserve(Session $s): ?int
    {
        if (!$s->date_heure_debut || !$s->date_heure_suspension) {
            return null;
        }

        return max(0, Carbon::parse($s->date_heure_suspension)->getTimestamp() - Carbon::parse($s->date_heure_debut)->getTimestamp());
    }
}