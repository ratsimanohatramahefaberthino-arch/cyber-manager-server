<?php

namespace App\Services\Moteur;

use App\Services\ReseauService;
use InvalidArgumentException;
use RuntimeException;

/**
 * Choix des ports du moteur, par TEST RÉEL (jamais de supposition).
 *
 * Un port est « libre » si :
 *   1. personne n'y répond (tentative de connexion sur 127.0.0.1 et sur les adresses de ce PC) ;
 *   2. on peut réellement y écouter.
 * La deuxième vérification seule ne suffit pas sous Windows : l'écoute peut réussir sur un port déjà pris
 * par un autre programme qui partage l'adresse.
 */
class ChoixPorts
{
    public const WORKERS_MIN = 2;
    public const WORKERS_MAX = 8;

    private const DELAI_SONDE_SECONDES = 0.3;
    private const ESSAIS_PAR_PORT      = 20;

    /** @param string[]|null $hotesSonde adresses à sonder pour un port HTTP (défaut : 127.0.0.1 + adresses de ce PC) */
    public function __construct(private ?array $hotesSonde = null)
    {
    }

    public function estLibre(int $port, string $hote = '0.0.0.0'): bool
    {
        if ($port < 1 || $port > 65535) {
            return false;
        }

        foreach ($this->hotesAProber($hote) as $adresse) {
            $client = @stream_socket_client("tcp://{$adresse}:{$port}", $errno, $errstr, self::DELAI_SONDE_SECONDES);
            if ($client !== false) {
                fclose($client);

                return false;   // quelqu'un répond déjà
            }
        }

        $serveur = @stream_socket_server("tcp://{$hote}:{$port}", $errno, $errstr);
        if ($serveur === false) {
            return false;
        }
        fclose($serveur);

        return true;
    }

    /**
     * Port HTTP : demandé (strict) sinon préféré, précédent, repli, puis un port choisi par le système.
     *
     * @param int[] $repli
     * @return array{port:int, origine:string} origine : demande, prefere, precedent, repli ou systeme
     */
    public function choisirHttp(int $prefere, array $repli = [], ?int $precedent = null, ?int $impose = null): array
    {
        if ($impose !== null) {
            if (!$this->estLibre($impose)) {
                throw new RuntimeException("Le port HTTP {$impose} demandé est déjà occupé par un autre programme.");
            }

            return ['port' => $impose, 'origine' => 'demande'];
        }

        $candidats = [];
        $ajouter = function (int $port, string $origine) use (&$candidats): void {
            if ($port >= 1 && $port <= 65535 && !isset($candidats[$port])) {
                $candidats[$port] = $origine;
            }
        };

        $ajouter($prefere, 'prefere');
        if ($precedent !== null) {
            $ajouter($precedent, 'precedent');
        }
        foreach ($repli as $port) {
            $ajouter((int) $port, 'repli');
        }

        foreach ($candidats as $port => $origine) {
            if ($this->estLibre($port)) {
                return ['port' => $port, 'origine' => $origine];
            }
        }

        [$socket, $port] = $this->reserverPortSysteme('0.0.0.0');
        fclose($socket);

        return ['port' => $port, 'origine' => 'systeme'];
    }

    /**
     * N ports FastCGI libres sur 127.0.0.1, distincts entre eux et de ceux à exclure.
     * Les ports de la préparation précédente sont réutilisés s'ils sont encore libres.
     *
     * @param int[] $exclus
     * @param int[] $precedents
     * @return int[] triés par ordre croissant
     */
    public function choisirFastcgi(int $n, array $exclus = [], array $precedents = []): array
    {
        if ($n < 1) {
            throw new InvalidArgumentException('Il faut au moins un port FastCGI.');
        }

        $exclus  = array_map('intval', $exclus);
        $retenus = [];

        foreach ($precedents as $port) {
            $port = (int) $port;
            if (count($retenus) >= $n) {
                break;
            }
            if (in_array($port, $exclus, true) || in_array($port, $retenus, true)) {
                continue;
            }
            if ($this->estLibre($port, '127.0.0.1')) {
                $retenus[] = $port;
            }
        }

        // Les sockets restent ouverts pendant le choix : le système ne peut pas nous donner deux fois le même port.
        $reservations = [];
        try {
            $essais = 0;
            while (count($retenus) < $n) {
                if (++$essais > $n * self::ESSAIS_PAR_PORT) {
                    throw new RuntimeException('Impossible de trouver assez de ports libres pour FastCGI.');
                }

                [$socket, $port] = $this->reserverPortSysteme('127.0.0.1');
                $reservations[] = $socket;

                if (in_array($port, $exclus, true) || in_array($port, $retenus, true)) {
                    continue;
                }
                $retenus[] = $port;
            }
        } finally {
            foreach ($reservations as $socket) {
                fclose($socket);
            }
        }

        sort($retenus);

        return $retenus;
    }

    /** Nombre de processus php-cgi : cœurs du PC en mode automatique, sinon valeur choisie, toujours entre 2 et 8. */
    public function nombreWorkers(bool $auto, int $manuel, ?int $coeurs = null): int
    {
        $n = $auto ? ($coeurs ?? self::coeursCpu()) : $manuel;

        return max(self::WORKERS_MIN, min(self::WORKERS_MAX, $n));
    }

    public static function coeursCpu(): int
    {
        $n = (int) (getenv('NUMBER_OF_PROCESSORS') ?: 0);
        if ($n > 0) {
            return $n;
        }

        if (is_readable('/proc/cpuinfo')) {
            $n = (int) preg_match_all('/^processor\s*:/m', (string) @file_get_contents('/proc/cpuinfo'));
            if ($n > 0) {
                return $n;
            }
        }

        return self::WORKERS_MIN;
    }

    /** @return array{0:resource, 1:int} socket ouvert (à fermer par l'appelant) et son port */
    private function reserverPortSysteme(string $hote): array
    {
        $socket = @stream_socket_server("tcp://{$hote}:0", $errno, $errstr);
        if ($socket === false) {
            throw new RuntimeException("Impossible d'ouvrir un port sur {$hote} : {$errstr}");
        }

        $nom  = (string) stream_socket_get_name($socket, false);
        $port = (int) substr($nom, (int) strrpos($nom, ':') + 1);

        return [$socket, $port];
    }

    /** @return string[] */
    private function hotesAProber(string $hote): array
    {
        if ($hote !== '0.0.0.0') {
            return [$hote];
        }

        $hotes = $this->hotesSonde ?? ReseauService::toutes();

        return array_values(array_unique(array_merge(['127.0.0.1'], $hotes)));
    }
}