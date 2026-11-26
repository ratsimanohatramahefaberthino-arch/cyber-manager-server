<?php

namespace App\Services;

use App\Models\Incident;
use App\Models\Session;
use App\Support\SessionTransparence;

/** Données du bandeau d'alerte (rendu initial + rafraîchissement JSON). */
final class BandeauIncidents
{
    /** @return array<string, mixed> */
    public static function donnees(): array
    {
        $horloge = ResilienceService::horlogeBloquee();
        $ouverts = Incident::ouverts()->orderBy('debut_at')->get();

        if (!$horloge && $ouverts->isEmpty()) {
            return ['visible' => false];
        }

        $suspendues = Session::query()
            ->whereIn('incident_id', $ouverts->pluck('id'))
            ->where('etat', SessionTransparence::ETAT_SUSPENDUE)
            ->get();

        $service = app(PosteSessionService::class);
        $montant = (int) $suspendues->sum(fn (Session $s) => $service->montant($s));

        $dureeMax = (int) $ouverts->max(fn (Incident $i) => $i->duree_secondes
            ?? max(0, now()->getTimestamp() - $i->debut_at->getTimestamp()));

        return [
            'visible'        => true,
            'horloge'        => $horloge,
            'nb_incidents'   => $ouverts->count(),
            'nb_sessions'    => $suspendues->count(),
            'montant_fige'   => $montant,
            'duree_max_s'    => $dureeMax,
            'url'            => $ouverts->count() === 1
                ? route('incidents.show', $ouverts->first())
                : route('incidents.index', ['statut' => 'ouvert']),
        ];
    }
}