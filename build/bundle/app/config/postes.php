<?php

return [

    /*
    |--------------------------------------------------------------------------
    | OBSOLÈTE : réglages remplacés par la page « Résilience » (ParametresResilience)
    |--------------------------------------------------------------------------
    |
    | - hors_ligne_tolerance_minutes  → abandon_poste_absent_minutes (défaut 120)
    |                                    et abandon_apres_retour_minutes (défaut 20)
    | - garde_redemarrage_secondes    → fenetre_reconnexion_minutes (défaut 8), persistée en base
    |
    | Ces clés ne sont plus lues par l'application. Elles sont conservées pour ne pas
    | casser d'éventuels .env existants.
    |
    */
    'hors_ligne_tolerance_minutes' => (int) env('POSTES_HORS_LIGNE_TOLERANCE_MIN', 10),
    'garde_redemarrage_secondes'   => (int) env('POSTES_GARDE_REDEMARRAGE', 90),

];