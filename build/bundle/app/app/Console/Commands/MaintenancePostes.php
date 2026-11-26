<?php

namespace App\Console\Commands;

use App\Models\Session;
use App\Services\PosteSessionService;
use App\Services\ResilienceService;
use Illuminate\Console\Command;
use Illuminate\Support\Facades\Cache;
use Illuminate\Support\Facades\Log;

/**
 * Maintenance des postes clients.
 *
 *  postes:maintenance           → expire les forfaits terminés, fige les sessions des postes injoignables
 *                                  (gel rétroactif au dernier signe de vie), abandonne les sessions figées
 *                                  jamais reprises (délais réglables : ParametresResilience)
 *  postes:maintenance --reseau  → réconcilie le routeur avec l'état réel des sessions
 *
 * Les verrous utilisent le cache « file » : aucune dépendance à la table cache de la base.
 */
class MaintenancePostes extends Command
{
    protected $signature = 'postes:maintenance {--reseau : Réconcilie l\'accès Internet des postes (autorisé / coupé)}';

    protected $description = 'Expire les forfaits, fige les postes injoignables, abandonne les sessions figées, réconcilie Internet.';

    public function handle(PosteSessionService $sessions, ResilienceService $resilience): int
    {
        $reseau = (bool) $this->option('reseau');

        // Un seul exemplaire à la fois (évite les doubles traitements si un passage traîne)
        $verrou = Cache::store('file')->lock($reseau ? 'postes:maintenance:reseau' : 'postes:maintenance', 120);
        if (!$verrou->get()) {
            return self::SUCCESS;
        }

        try {
            if ($reseau) {
                $this->reconcilierReseau($sessions);
            } elseif (ResilienceService::horlogeBloquee()) {
                // Horloge suspecte : aucun calcul automatique tant que le gérant n'a pas confirmé l'heure
                if (Cache::store('file')->add('postes:maintenance:horloge_log', 1, 600)) {
                    Log::warning('Maintenance des postes suspendue : horloge à vérifier (confirmation du gérant requise).');
                }
            } else {
                $this->expirerLesForfaits($sessions);

                // Pendant la fenêtre de reconnexion qui suit un redémarrage du serveur, on laisse
                // les PC revenir : le gel est rétroactif, rien n'est perdu en attendant.
                if (!$resilience->fenetreReconnexionActive()) {
                    $this->figerLesPostesInjoignables($resilience);
                    $this->abandonnerLesSessionsFigees($sessions);
                }
            }
        } catch (\Throwable $e) {
            Log::error('Maintenance des postes : erreur inattendue : ' . $e->getMessage());
            return self::FAILURE;
        } finally {
            $verrou->release();
        }

        return self::SUCCESS;
    }

    /** Forfaits dont le temps est écoulé (postes en ligne ou non). */
    private function expirerLesForfaits(PosteSessionService $sessions): void
    {
        $candidates = Session::query()
            ->where('etat', 'en_cours')
            ->where('mode_facturation', 'forfait')
            ->where('duree_prevue', '>', 0)
            ->whereNotNull('poste_id')
            ->with('poste')
            ->get();

        foreach ($candidates as $session) {
            $poste = $session->poste;
            if ($poste === null || !$sessions->sessionExpiree($session)) {
                continue;
            }
            $sessions->expirerSiNecessaire($poste);
        }
    }

    /** Événement A : sessions « en cours » dont le poste ne donne plus signe de vie. */
    private function figerLesPostesInjoignables(ResilienceService $resilience): void
    {
        $enCours = Session::query()
            ->where('etat', 'en_cours')
            ->whereNotNull('poste_id')
            ->with('poste')
            ->get();

        foreach ($enCours as $session) {
            $poste = $session->poste;
            if ($poste === null || $poste->estEnLigne()) {
                continue;
            }
            $resilience->figerPosteInjoignable($poste);
        }
    }

    /** Sessions figées par une coupure et jamais reprises dans les délais réglés. */
    private function abandonnerLesSessionsFigees(PosteSessionService $sessions): void
    {
        $figees = Session::query()
            ->interrompues()
            ->whereNotNull('poste_id')
            ->with('poste')
            ->get();

        foreach ($figees as $session) {
            if ($session->poste !== null) {
                $sessions->abandonnerSiNecessaire($session->poste);
            }
        }
    }

    private function reconcilierReseau(PosteSessionService $sessions): void
    {
        $bilan = $sessions->reconcilierReseau();

        if ($bilan['connectes'] > 0 || $bilan['coupes'] > 0) {
            Log::info("Réconciliation Internet : {$bilan['connectes']} poste(s) autorisé(s), {$bilan['coupes']} coupé(s).");
        }
    }
}