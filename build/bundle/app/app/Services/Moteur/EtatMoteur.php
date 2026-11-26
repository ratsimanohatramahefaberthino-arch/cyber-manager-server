<?php

namespace App\Services\Moteur;

use InvalidArgumentException;
use RuntimeException;

/**
 * Emplacements du moteur (bundle, données) et lecture/écriture sécurisée de ports.json.
 *
 * Production : code en lecture seule, données dans %ProgramData%\CyberManager (data, runtime, logs).
 * Rien n'est imposé : CYBER_DONNEES_DIR, CYBER_CADDY et CYBER_PHP_CGI permettent de tout déplacer.
 *
 * ports.json est la source de vérité du futur service Windows (étape 3).
 */
class EtatMoteur
{
    public const VERSION = 1;

    public function __construct(private ?string $racine = null)
    {
    }

    // ------------------------------------------------------------------
    // Emplacements
    // ------------------------------------------------------------------

    public function racine(): string
    {
        if ($this->racine !== null && $this->racine !== '') {
            return rtrim($this->racine, '\\/');
        }

        $force = self::variable('CYBER_DONNEES_DIR');
        if ($force !== null) {
            return rtrim($force, '\\/');
        }

        $programData = self::variable('ProgramData');
        if ($programData !== null) {
            return rtrim($programData, '\\/') . DIRECTORY_SEPARATOR . 'CyberManager';
        }

        return storage_path('app' . DIRECTORY_SEPARATOR . 'cyber-manager');
    }

    public function dossierData(): string
    {
        return $this->racine() . DIRECTORY_SEPARATOR . 'data';
    }

    public function dossierRuntime(): string
    {
        return $this->racine() . DIRECTORY_SEPARATOR . 'runtime';
    }

    public function dossierLogs(): string
    {
        return $this->racine() . DIRECTORY_SEPARATOR . 'logs';
    }

    public function fichierPorts(): string
    {
        return $this->dossierRuntime() . DIRECTORY_SEPARATOR . 'ports.json';
    }

    public function fichierCaddyfile(): string
    {
        return $this->dossierRuntime() . DIRECTORY_SEPARATOR . 'Caddyfile';
    }

    public function cheminCaddy(): string
    {
        return self::variable('CYBER_CADDY')
            ?? base_path('runtime' . DIRECTORY_SEPARATOR . 'caddy' . DIRECTORY_SEPARATOR . 'caddy.exe');
    }

    public function cheminPhpCgi(): string
    {
        return self::variable('CYBER_PHP_CGI')
            ?? base_path('runtime' . DIRECTORY_SEPARATOR . 'php' . DIRECTORY_SEPARATOR . 'php-cgi.exe');
    }

    /** php.exe (ligne de commande) placé à côté de php-cgi.exe, s'il existe : sert au service pour lancer artisan. */
    public function cheminPhpCli(): ?string
    {
        $candidat = dirname($this->cheminPhpCgi()) . DIRECTORY_SEPARATOR . 'php.exe';

        return is_file($candidat) ? $candidat : null;
    }

    public function assurerDossiers(): void
    {
        foreach ([$this->dossierData(), $this->dossierRuntime(), $this->dossierLogs()] as $dossier) {
            if (!is_dir($dossier) && !@mkdir($dossier, 0775, true) && !is_dir($dossier)) {
                throw new RuntimeException("Impossible de créer le dossier : {$dossier}");
            }
        }
    }

    // ------------------------------------------------------------------
    // ports.json
    // ------------------------------------------------------------------

    /** @return array<string,mixed>|null null si absent, illisible, d'une autre version ou incohérent */
    public function lire(): ?array
    {
        $fichier = $this->fichierPorts();
        if (!is_file($fichier)) {
            return null;
        }

        $brut = @file_get_contents($fichier);
        if ($brut === false) {
            return null;
        }

        $json = json_decode($brut, true);

        return (is_array($json) && self::estValide($json)) ? $json : null;
    }

    /** Écriture atomique : fichier temporaire puis remplacement. Un état invalide n'est jamais écrit. */
    public function ecrire(array $etat): void
    {
        if (!self::estValide($etat)) {
            throw new InvalidArgumentException('État du moteur invalide : ports.json non écrit.');
        }

        $fichier = $this->fichierPorts();
        $dossier = dirname($fichier);
        if (!is_dir($dossier) && !@mkdir($dossier, 0775, true) && !is_dir($dossier)) {
            throw new RuntimeException("Impossible de créer le dossier : {$dossier}");
        }

        $json = json_encode($etat, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_THROW_ON_ERROR);

        $temporaire = $fichier . '.tmp.' . getmypid() . '.' . bin2hex(random_bytes(3));
        if (file_put_contents($temporaire, $json . PHP_EOL, LOCK_EX) === false) {
            throw new RuntimeException("Impossible d'écrire : {$temporaire}");
        }

        if (!self::renommerAvecReprise($temporaire, $fichier)) {
            @unlink($temporaire);
            throw new RuntimeException("Impossible de remplacer : {$fichier}");
        }
    }

    /** Vrai si le Caddyfile sur disque est bien celui que décrit ports.json (détecte une coupure entre les deux écritures). */
    public function coherent(array $etat): bool
    {
        $caddyfile = (string) ($etat['caddyfile'] ?? $this->fichierCaddyfile());
        $attendu   = $etat['caddyfile_sha256'] ?? null;

        if (!is_file($caddyfile) || !is_string($attendu) || $attendu === '') {
            return false;
        }

        return hash_file('sha256', $caddyfile) === $attendu;
    }

    /**
     * Remplace $vers par $de. Sous Windows, le remplacement échoue si un autre programme (le service qui lit ports.json)
     * a le fichier ouvert à cet instant : on réessaie brièvement.
     */
    public static function renommerAvecReprise(string $de, string $vers, int $essais = 10): bool
    {
        for ($i = 0; $i < $essais; $i++) {
            if (@rename($de, $vers)) {
                return true;
            }
            usleep(100000);
        }

        return false;
    }

    // ------------------------------------------------------------------
    // Outils
    // ------------------------------------------------------------------

    private static function estValide(array $etat): bool
    {
        if (($etat['version'] ?? null) !== self::VERSION) {
            return false;
        }

        $http = $etat['http_port'] ?? null;
        if (!is_int($http) || $http < 1 || $http > 65535) {
            return false;
        }

        $fastcgi = $etat['fastcgi_ports'] ?? null;
        if (!is_array($fastcgi) || $fastcgi === []) {
            return false;
        }
        foreach ($fastcgi as $port) {
            if (!is_int($port) || $port < 1 || $port > 65535) {
                return false;
            }
        }

        return true;
    }

    private static function variable(string $nom): ?string
    {
        $valeur = getenv($nom);
        if ($valeur === false || $valeur === '') {
            $valeur = $_ENV[$nom] ?? null;
        }

        return (is_string($valeur) && $valeur !== '') ? $valeur : null;
    }
}