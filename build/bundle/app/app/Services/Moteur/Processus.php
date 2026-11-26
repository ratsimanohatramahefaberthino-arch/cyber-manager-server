<?php

namespace App\Services\Moteur;

/**
 * Lance un programme externe (caddy.exe, php-cgi.exe) avec un délai maximal.
 *
 * Les sorties passent par des fichiers temporaires : sous Windows, les tubes de proc_open ne peuvent pas
 * être lus sans bloquer, ce qui empêcherait de respecter le délai.
 */
final class Processus
{
    /**
     * @param string[] $commande Programme puis arguments (aucun shell n'est utilisé : pas de problème d'espaces ni de guillemets)
     * @return array{code:?int, sortie:string, erreur:string, delai_depasse:bool}
     */
    public static function executer(array $commande, int $delaiSecondes = 15): array
    {
        $fichierSortie = tempnam(sys_get_temp_dir(), 'cmo');
        $fichierErreur = tempnam(sys_get_temp_dir(), 'cme');

        if ($fichierSortie === false || $fichierErreur === false) {
            return ['code' => null, 'sortie' => '', 'erreur' => 'Impossible de créer un fichier temporaire.', 'delai_depasse' => false];
        }

        try {
            $descripteurs = [
                0 => ['pipe', 'r'],
                1 => ['file', $fichierSortie, 'w'],
                2 => ['file', $fichierErreur, 'w'],
            ];

            $pipes   = [];
            $process = @proc_open($commande, $descripteurs, $pipes, null, null, ['bypass_shell' => true]);
            if (!is_resource($process)) {
                return ['code' => null, 'sortie' => '', 'erreur' => 'Impossible de lancer : ' . ($commande[0] ?? '?'), 'delai_depasse' => false];
            }

            if (isset($pipes[0]) && is_resource($pipes[0])) {
                fclose($pipes[0]);
            }

            $limite  = microtime(true) + max(1, $delaiSecondes);
            $depasse = false;
            $code    = null;

            while (true) {
                $statut = proc_get_status($process);
                if (!$statut['running']) {
                    $code = (int) $statut['exitcode'];   // valable uniquement au premier appel qui voit la fin
                    break;
                }
                if (microtime(true) > $limite) {
                    $depasse = true;
                    proc_terminate($process);
                    break;
                }
                usleep(50000);
            }

            proc_close($process);

            return [
                'code'          => $depasse ? null : $code,
                'sortie'        => (string) @file_get_contents($fichierSortie),
                'erreur'        => (string) @file_get_contents($fichierErreur),
                'delai_depasse' => $depasse,
            ];
        } finally {
            @unlink($fichierSortie);
            @unlink($fichierErreur);
        }
    }
}