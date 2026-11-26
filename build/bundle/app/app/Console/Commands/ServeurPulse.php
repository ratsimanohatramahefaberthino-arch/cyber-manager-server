<?php

namespace App\Console\Commands;

use App\Services\ResilienceService;
use Illuminate\Console\Command;
use Illuminate\Support\Facades\Log;

class ServeurPulse extends Command
{
    protected $signature = 'serveur:pulse';

    protected $description = 'Écrit le pouls du serveur (dernier signe de vie, base de la détection de coupure).';

    public function handle(ResilienceService $resilience): int
    {
        try {
            $resilience->pulse();
        } catch (\Throwable $e) {
            // Un pouls manqué n'est pas grave (le suivant arrive 10 s plus tard) : pas de bruit.
            Log::warning('Pouls du serveur non écrit : ' . $e->getMessage());
        }

        return self::SUCCESS;
    }
}