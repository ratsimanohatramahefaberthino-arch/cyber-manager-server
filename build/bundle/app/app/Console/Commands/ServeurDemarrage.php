<?php

namespace App\Console\Commands;

use App\Services\ResilienceService;
use Illuminate\Console\Command;
use Illuminate\Support\Facades\Log;

class ServeurDemarrage extends Command
{
    protected $signature = 'serveur:demarrage {--json : Résultat au format JSON (pour le service Windows)}';

    protected $description = 'Au démarrage du serveur : détecte une coupure, fige les sessions au dernier signe de vie, ouvre la fenêtre de reconnexion.';

    public function handle(ResilienceService $resilience): int
    {
        try {
            $bilan = $resilience->traiterDemarrageServeur();
        } catch (\Throwable $e) {
            Log::error('Traitement du démarrage du serveur échoué : ' . $e->getMessage());
            $this->error('Échec : ' . $e->getMessage());
            return self::FAILURE;
        }

        if ($this->option('json')) {
            $this->line(json_encode($bilan, JSON_UNESCAPED_UNICODE));
            return self::SUCCESS;
        }

        $this->info("Démarrage : {$bilan['type']} (écart {$bilan['ecart_secondes']} s)");
        if ($bilan['sessions_figees'] > 0) {
            $this->warn("{$bilan['sessions_figees']} session(s) figée(s), {$bilan['montant_fige']} Ar figés.");
        }

        return self::SUCCESS;
    }
}