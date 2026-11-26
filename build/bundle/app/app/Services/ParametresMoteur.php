<?php

namespace App\Services;

use App\Models\CyberSetting;
use InvalidArgumentException;

/**
 * Réglages du moteur (Caddy + php-cgi), modifiables depuis l'interface. Même modèle que ParametresResilience :
 * stockés dans cyber_settings (clés « moteur.* »), valeurs bornées.
 */
final class ParametresMoteur
{
    private const PREFIXE      = 'moteur.';
    private const TTL_SECONDES = 5;
    private const PORTS_MAX    = 12;

    /** @var array<string, array{type:string, defaut:mixed, min?:int, max?:int}> */
    public const DEFINITIONS = [
        'port_http_prefere' => ['type' => 'entier',  'defaut' => 80,                       'min' => 1, 'max' => 65535],
        'ports_http_repli'  => ['type' => 'ports',   'defaut' => [8080, 8000, 8081, 8888]],
        'workers_auto'      => ['type' => 'booleen', 'defaut' => true],
        'workers_nombre'    => ['type' => 'entier',  'defaut' => 4,                        'min' => 2, 'max' => 8],
    ];

    private static ?array $memo = null;
    private static int $memoAt  = 0;

    /** @return array<string, mixed> */
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

    /** @param mixed $valeur pour « ports_http_repli » : tableau ou texte « 8080, 8000 » (saisie stricte : tout port invalide est refusé) */
    public static function definir(string $cle, mixed $valeur): void
    {
        $def = self::DEFINITIONS[$cle] ?? throw new InvalidArgumentException("Réglage inconnu : {$cle}");

        if ($def['type'] === 'ports') {
            $texte = is_array($valeur) ? implode(',', $valeur) : (string) $valeur;
            $liste = self::listePorts($texte, true);
            if ($liste === []) {
                throw new InvalidArgumentException('Indiquez au moins un port de repli.');
            }
            CyberSetting::definir(self::PREFIXE . $cle, implode(',', $liste));
            self::oublier();

            return;
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
            'ports'   => self::listePorts((string) $valeur, false) ?: $def['defaut'],
            'booleen' => in_array(strtolower((string) $valeur), ['1', 'true', 'oui', 'on', 'yes'], true),
            default   => max($def['min'], min($def['max'], (int) $valeur)),
        };
    }

    /**
     * @return int[] ports valides, sans doublon, dans l'ordre saisi
     * @throws InvalidArgumentException en mode strict si un élément est invalide ou si la liste est trop longue
     */
    private static function listePorts(string $texte, bool $strict): array
    {
        $morceaux = preg_split('/[\s,;]+/', trim($texte), -1, PREG_SPLIT_NO_EMPTY) ?: [];

        $ports = [];
        foreach ($morceaux as $morceau) {
            $valide = ctype_digit($morceau) && (int) $morceau >= 1 && (int) $morceau <= 65535;
            if (!$valide) {
                if ($strict) {
                    throw new InvalidArgumentException("Port invalide : « {$morceau} » (entier entre 1 et 65535).");
                }
                continue;
            }
            $ports[(int) $morceau] = true;
        }

        $ports = array_keys($ports);
        if ($strict && count($ports) > self::PORTS_MAX) {
            throw new InvalidArgumentException('Trop de ports de repli (' . self::PORTS_MAX . ' au maximum).');
        }

        return array_slice($ports, 0, self::PORTS_MAX);
    }

    // ---- Accesseurs typés ----------------------------------------------------

    public static function portHttpPrefere(): int { return (int) self::get('port_http_prefere'); }

    /** @return int[] */
    public static function portsHttpRepli(): array { return array_map('intval', (array) self::get('ports_http_repli')); }

    public static function workersAuto(): bool { return (bool) self::get('workers_auto'); }

    public static function workersNombre(): int { return (int) self::get('workers_nombre'); }
}