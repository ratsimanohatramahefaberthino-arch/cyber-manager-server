<?php

namespace App\Console\Commands;

use App\Services\CodeInstallation;
use Illuminate\Console\Command;

class CodeInstallationAfficher extends Command
{
    protected $signature = 'cyber:code-installation';

    protected $description = "Affiche le code d'installation des postes (utilisé par la télécommande du serveur)";

    public function handle(): int
    {
        try {
            $this->line(CodeInstallation::obtenir());

            return self::SUCCESS;
        } catch (\Throwable $e) {
            $this->error('Code d\'installation indisponible : ' . $e->getMessage());

            return self::FAILURE;
        }
    }
}