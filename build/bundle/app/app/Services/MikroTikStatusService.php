<?php

namespace App\Services;

use Illuminate\Support\Facades\Cache;

/**
 * Statut de connexion MikroTik pour la barre supérieure.
 * Durée de cache courte (8 s) pour une réactivité immédiate sans ralentir les requêtes.
 */
class MikroTikStatusService
{
    private const CLE = 'mikrotik.statut';
    private const TTL = 8;

    public function __construct(
        private MikroTikService $mikroTik,
    ) {
    }

    /**
     * @return array{configure:bool, connecte:bool, identity:?string, version:?string, erreur:?string, verifie_a:string}
     */
    public function obtenir(): array
    {
        if (!$this->mikroTik->estConfigure()) {
            return [
                'configure' => false,
                'connecte'  => false,
                'identity'  => null,
                'version'   => null,
                'erreur'    => null,
                'verifie_a' => now()->toIso8601String(),
            ];
        }

        return Cache::remember(self::CLE, self::TTL, function () {
            try {
                $info = $this->mikroTik->testConnection();

                return [
                    'configure' => true,
                    'connecte'  => true,
                    'identity'  => $info['identity'],
                    'version'   => $info['version'],
                    'erreur'    => null,
                    'verifie_a' => now()->toIso8601String(),
                ];
            } catch (\Throwable $e) {
                return [
                    'configure' => true,
                    'connecte'  => false,
                    'identity'  => null,
                    'version'   => null,
                    'erreur'    => mb_substr($e->getMessage(), 0, 200),
                    'verifie_a' => now()->toIso8601String(),
                ];
            }
        });
    }
}