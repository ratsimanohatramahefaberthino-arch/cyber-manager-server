<?php

namespace App\Console\Commands;

use App\Models\CyberSetting;
use App\Services\ResilienceService;
use Illuminate\Console\Command;

class ServeurArretPropre extends Command
{
    protected $signature = 'serveur:arret-propre {propre=1 : 1 pour arret propre, 0 pour anormal}';
    protected $description = 'Écrit le statut arret_propre dans cyber_settings (pour le service Windows)';

    public function handle(): int
    {
        $valeur = $this->argument('propre') === '1' ? '1' : '0';
        CyberSetting::definir(ResilienceService::CLE_ARRET_PROPRE, $valeur);

        return self::SUCCESS;
    }
}