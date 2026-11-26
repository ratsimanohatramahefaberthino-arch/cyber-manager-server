<?php

namespace App\Services;

use App\Models\CyberSetting;
use InvalidArgumentException;

/**
 * Réglages de résilience aux coupures, modifiables depuis l'interface (jamais en dur).
 * Stockés dans cyber_settings, clés « resilience.* ». Valeurs bornées : une saisie
 * aberrante ne peut jamais casser le serveur.
 */
final class ParametresResilience
{
    public const POLITIQUE_CLIENT = 'client';
    public const POLITIQUE_GERANT = 'gerant';
    public const POLITIQUE_AUTO   = 'auto';

    public const POLITIQUES = [
        self::POLITIQUE_CLIENT,
        self::POLITIQUE_GERANT,
        self::POLITIQUE_AUTO,
    ];

    private const PREFIXE      = 'resilience.';
    private const TTL_SECONDES = 5;

    /** @var array<string, array{type:string, defaut:mixed, min?:int, max?:int}> */
    public const DEFINITIONS = [
        'politique_reprise'            => ['type' => 'politique', 'defaut' => self::POLITIQUE_CLIENT],
        'delai_injoignable_secondes'   => ['type' => 'entier', 'defaut' => 180,   'min' => 60,   'max' => 900],
        'delai_grace_agent_secondes'   => ['type' => 'entier', 'defaut' => 180,   'min' => 60,   'max' => 900],
        'fenetre_reconnexion_minutes'  => ['type' => 'entier', 'defaut' => 8,     'min' => 1,    'max' => 30],
        'abandon_apres_retour_minutes' => ['type' => 'entier', 'defaut' => 20,    'min' => 0,    'max' => 240],
        'abandon_poste_absent_minutes' => ['type' => 'entier', 'defaut' => 120,   'min' => 0,    'max' => 1440],
        'port_decouverte_udp'          => ['type' => 'entier', 'defaut' => 47474, 'min' => 1024, 'max' => 65535],
        'horloge_garde_active'         => ['type' => 'booleen', 'defaut' => true],
    ];

    private static ?array $memo = null;
    private static int $memoAt  = 0;

    /** @return array<string, mixed> tous les réglages, valeurs propres et bornées */
    public static function tous(): array
    {
        if (self::$memo !== null && (time() - self::$memoAt) < self::TTL_SECONDES) {
            return self::$memo;
        }

        $brut = [];
        try {
            $brut = CyberSetting::query()
                ->where('cle', 'like', self::PREFIXE . '%')
                ->pluck('valeur', 'cle')
                ->all();
        } catch (\Throwable) {
            // table absente (avant migration) : valeurs par défaut
        }

        $res = [];
        foreach (self::DEFINITIONS as $cle => $def) {
            $res[$cle] = self::normaliser($def, $brut[self::PREFIXE . $cle] ?? null);
        }

        self::$memo   = $res;
        self::$memoAt = time();

        return $res;
    }

    public static function get(string $cle): mixed
    {
        return self::tous()[$cle] ?? (self::DEFINITIONS[$cle]['defaut'] ?? null);
    }

    public static function definir(string $cle, mixed $valeur): void
    {
        $def = self::DEFINITIONS[$cle] ?? throw new InvalidArgumentException("Réglage inconnu : {$cle}");

        if ($def['type'] === 'politique' && !in_array($valeur, self::POLITIQUES, true)) {
            throw new InvalidArgumentException('Politique de reprise invalide (client, gerant ou auto).');
        }

        $propre = self::normaliser($def, is_bool($valeur) ? ($valeur ? '1' : '0') : (string) $valeur);

        CyberSetting::definir(self::PREFIXE . $cle, is_bool($propre) ? ($propre ? '1' : '0') : (string) $propre);
        self::oublier();
    }

    /** Vide le cache du processus (après une modification, et dans les tests). */
    public static function oublier(): void
    {
        self::$memo   = null;
        self::$memoAt = 0;
    }

    private static function normaliser(array $def, mixed $valeur): mixed
    {
        if ($valeur === null || $valeur === '') {
            return $def['defaut'];
        }

        return match ($def['type']) {
            'politique' => in_array($valeur, self::POLITIQUES, true) ? $valeur : $def['defaut'],
            'booleen'   => in_array(strtolower((string) $valeur), ['1', 'true', 'oui', 'on', 'yes'], true),
            default     => max($def['min'], min($def['max'], (int) $valeur)),
        };
    }

    // ---- Accesseurs typés ----------------------------------------------------

    public static function politiqueReprise(): string        { return (string) self::get('politique_reprise'); }
    public static function delaiInjoignableSecondes(): int   { return (int) self::get('delai_injoignable_secondes'); }
    public static function delaiGraceAgentSecondes(): int    { return (int) self::get('delai_grace_agent_secondes'); }
    public static function fenetreReconnexionMinutes(): int  { return (int) self::get('fenetre_reconnexion_minutes'); }
    public static function abandonApresRetourMinutes(): int  { return (int) self::get('abandon_apres_retour_minutes'); }
    public static function abandonPosteAbsentMinutes(): int  { return (int) self::get('abandon_poste_absent_minutes'); }
    public static function portDecouverteUdp(): int          { return (int) self::get('port_decouverte_udp'); }
    public static function horlogeGardeActive(): bool        { return (bool) self::get('horloge_garde_active'); }
}