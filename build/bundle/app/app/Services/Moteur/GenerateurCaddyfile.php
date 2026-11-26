<?php

namespace App\Services\Moteur;

use InvalidArgumentException;
use RuntimeException;

/**
 * Génération, validation (par caddy.exe lui-même) et écriture atomique du Caddyfile.
 * Aucune adresse IP de carte réseau n'est écrite : « :PORT » écoute sur toutes les cartes (mono ou multi-cartes).
 */
class GenerateurCaddyfile
{
    /**
     * @param int[] $portsFastcgi
     * @param bool  $robuste      vrai : réessais et mise à l'écart d'un worker en panne ; faux : profil minimal
     */
    public function generer(int $portHttp, array $portsFastcgi, string $racinePublique, ?string $fichierLog = null, bool $robuste = true): string
    {
        $this->verifierPort($portHttp, 'HTTP');

        $portsFastcgi = array_values(array_unique(array_map('intval', $portsFastcgi)));
        if ($portsFastcgi === []) {
            throw new InvalidArgumentException('Au moins un port FastCGI est nécessaire.');
        }
        foreach ($portsFastcgi as $port) {
            $this->verifierPort($port, 'FastCGI');
            if ($port === $portHttp) {
                throw new InvalidArgumentException("Le port FastCGI {$port} ne peut pas être celui du serveur HTTP.");
            }
        }

        $racine    = $this->chemin($racinePublique);
        $log       = $fichierLog !== null ? $this->chemin($fichierLog) : null;
        $upstreams = implode(' ', array_map(fn (int $p) => "127.0.0.1:{$p}", $portsFastcgi));

        $l   = [];
        $l[] = '# Fichier genere automatiquement par « php artisan serveur:preparer ».';
        $l[] = '# Ne pas modifier a la main : il est recree a chaque preparation du moteur.';
        $l[] = '{';
        $l[] = "\tadmin off";
        $l[] = "\tauto_https off";
        if ($log !== null) {
            $l[] = "\tlog {";
            $l[] = "\t\toutput file \"{$log}\" {";
            $l[] = "\t\t\troll_size 5MiB";
            $l[] = "\t\t\troll_keep 3";
            $l[] = "\t\t}";
            $l[] = "\t\tlevel WARN";
            $l[] = "\t}";
        }
        $l[] = '}';
        $l[] = '';
        $l[] = ":{$portHttp} {";
        $l[] = "\troot * \"{$racine}\"";
        $l[] = "\tencode gzip";
        $l[] = "\tphp_fastcgi {$upstreams} {";
        $l[] = "\t\tlb_policy least_conn";
        if ($robuste) {
            $l[] = "\t\tlb_try_duration 3s";
            $l[] = "\t\tlb_try_interval 250ms";
            $l[] = "\t\tfail_duration 15s";
            $l[] = "\t\tmax_fails 1";
        }
        $l[] = "\t}";
        $l[] = "\tfile_server";
        $l[] = '}';

        return implode("\n", $l) . "\n";
    }

    /** Demande à Caddy lui-même si la configuration est valide (rien n'est démarré ni écrit à l'emplacement final). */
    public function valider(string $contenu, string $caddyExe): array
    {
        $temporaire = tempnam(sys_get_temp_dir(), 'cmcf');
        if ($temporaire === false) {
            return ['valide' => false, 'message' => 'Impossible de créer un fichier temporaire.'];
        }

        try {
            file_put_contents($temporaire, $contenu);

            $r = Processus::executer([$caddyExe, 'validate', '--config', $temporaire, '--adapter', 'caddyfile'], 30);

            if ($r['delai_depasse']) {
                return ['valide' => false, 'message' => 'Caddy n\'a pas répondu dans le délai imparti.'];
            }
            if ($r['code'] === 0) {
                return ['valide' => true, 'message' => 'Configuration valide.'];
            }

            $message = trim($r['erreur'] !== '' ? $r['erreur'] : $r['sortie']);

            return ['valide' => false, 'message' => mb_substr($message, 0, 800)];
        } finally {
            @unlink($temporaire);
        }
    }

    /**
     * Écriture atomique : fichier temporaire puis remplacement, avec copie de l'ancienne version en .bak.
     * Si le contenu est identique, rien n'est touché.
     *
     * @return array{modifie:bool, sauvegarde:?string}
     */
    public function installer(string $contenu, string $destination): array
    {
        $dossier = dirname($destination);
        if (!is_dir($dossier) && !@mkdir($dossier, 0775, true) && !is_dir($dossier)) {
            throw new RuntimeException("Impossible de créer le dossier : {$dossier}");
        }

        $actuel = is_file($destination) ? @file_get_contents($destination) : null;
        if ($actuel === $contenu) {
            return ['modifie' => false, 'sauvegarde' => null];
        }

        $sauvegarde = null;
        if (is_string($actuel)) {
            $sauvegarde = $destination . '.bak';
            if (!@copy($destination, $sauvegarde)) {
                $sauvegarde = null;
            }
        }

        $temporaire = $destination . '.tmp.' . getmypid() . '.' . bin2hex(random_bytes(3));
        if (file_put_contents($temporaire, $contenu, LOCK_EX) === false) {
            throw new RuntimeException("Impossible d'écrire : {$temporaire}");
        }

        if (!EtatMoteur::renommerAvecReprise($temporaire, $destination)) {
            @unlink($temporaire);
            throw new RuntimeException("Impossible de remplacer : {$destination}");
        }

        return ['modifie' => true, 'sauvegarde' => $sauvegarde];
    }

    private function verifierPort(int $port, string $nom): void
    {
        if ($port < 1 || $port > 65535) {
            throw new InvalidArgumentException("Port {$nom} invalide : {$port}.");
        }
    }

    /** Chemin sûr pour un Caddyfile : barres obliques, et refus de ce qui casserait la syntaxe. */
    private function chemin(string $chemin): string
    {
        if ($chemin === '' || preg_match('/["\r\n{}]/', $chemin)) {
            throw new InvalidArgumentException("Chemin refusé dans le Caddyfile : {$chemin}");
        }

        return str_replace('\\', '/', $chemin);
    }
}