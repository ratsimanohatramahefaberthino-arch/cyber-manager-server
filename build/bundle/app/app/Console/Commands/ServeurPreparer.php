<?php

namespace App\Console\Commands;

use App\Services\Moteur\ChoixPorts;
use App\Services\Moteur\EtatMoteur;
use App\Services\Moteur\GenerateurCaddyfile;
use App\Services\Moteur\Processus;
use App\Services\ParametresMoteur;
use App\Services\ParametresResilience;
use App\Services\ReseauService;
use Illuminate\Console\Command;
use RuntimeException;

/**
 * Prépare le moteur d'exécution autonome (Caddy + pool php-cgi). À lancer MOTEUR ARRÊTÉ (le service Windows le fera).
 *
 *  1. contrôle le bundle (caddy.exe, php-cgi.exe) et lance de vrais tests dessus ;
 *  2. choisit les ports par test réel (HTTP puis FastCGI) ;
 *  3. génère le Caddyfile, le fait valider par Caddy, l'écrit de façon atomique ;
 *  4. écrit ports.json (source de vérité du service Windows) EN DERNIER.
 *
 * En cas d'erreur, RIEN n'est écrit : le dernier état valide reste en place.
 */
class ServeurPreparer extends Command
{
    /** Extensions exigées de php-cgi (« openssl » : chiffrement de Laravel, APP_KEY). */
    private const EXTENSIONS_REQUISES = ['pdo_sqlite', 'curl', 'mbstring', 'sockets', 'fileinfo', 'openssl'];

    private const PHP_MINIMUM = '8.2.0';

    /** Un worker php-cgi se termine proprement après ce nombre de requêtes (le service le relance) : évite les fuites mémoire. */
    private const FCGI_MAX_REQUESTS = '500';

    protected $signature = 'serveur:preparer
        {--json : Resultat au format JSON (pour le service Windows)}
        {--simuler : Calcule et teste tout, sans rien ecrire}
        {--http-port= : Impose le port HTTP (echoue s il est occupe)}
        {--workers= : Impose le nombre de processus php-cgi (2 a 8)}';

    protected $description = 'Prépare le moteur autonome : ports libres, Caddyfile validé, ports.json';

    public function handle(EtatMoteur $etat, ChoixPorts $ports, GenerateurCaddyfile $generateur): int
    {
        $simuler = (bool) $this->option('simuler');

        $bilan = [
            'ok'                   => false,
            'simulation'           => $simuler,
            'erreurs'              => [],
            'avertissements'       => [],
            'tests'                => [],
            'etat'                 => null,
            'modifie'              => null,
            'caddyfile_modifie'    => null,
            'sauvegarde_caddyfile' => null,
        ];

        try {
            $httpImpose    = $this->optionEntiere('http-port', 1, 65535);
            $workersImpose = $this->optionEntiere('workers', ChoixPorts::WORKERS_MIN, ChoixPorts::WORKERS_MAX);
        } catch (RuntimeException $e) {
            $bilan['erreurs'][] = $e->getMessage();

            return $this->terminer($bilan);
        }

        $caddy          = $etat->cheminCaddy();
        $phpCgi         = $etat->cheminPhpCgi();
        $racinePublique = realpath(public_path()) ?: public_path();

        // 1. Bundle et tests réels des programmes
        $caddyPresent = is_file($caddy);
        $phpPresent   = is_file($phpCgi);

        if (!$caddyPresent) {
            $bilan['erreurs'][] = "caddy.exe introuvable : {$caddy}";
        }
        if (!$phpPresent) {
            $bilan['erreurs'][] = "php-cgi.exe introuvable : {$phpCgi}";
        }
        if (!is_file($racinePublique . DIRECTORY_SEPARATOR . 'index.php')) {
            $bilan['erreurs'][] = "public/index.php introuvable dans : {$racinePublique}";
        }

        if ($caddyPresent) {
            $this->testerCaddy($caddy, $bilan);
        }
        if ($phpPresent) {
            $this->testerPhpCgi($phpCgi, $bilan);
        }

        // 2. Cartes réseau (information : Caddy écoute sur toutes)
        $interfaces = ReseauService::toutes();
        if ($interfaces === []) {
            $bilan['avertissements'][] = 'Aucune carte réseau détectée : le serveur écoutera quand même sur toutes les interfaces.';
        }

        // 3. Ports
        $precedent = $etat->lire();

        try {
            $http = $ports->choisirHttp(
                ParametresMoteur::portHttpPrefere(),
                ParametresMoteur::portsHttpRepli(),
                $precedent['http_port'] ?? null,
                $httpImpose
            );

            $nombre = $workersImpose ?? $ports->nombreWorkers(ParametresMoteur::workersAuto(), ParametresMoteur::workersNombre());
            $fastcgi = $ports->choisirFastcgi($nombre, [$http['port']], $precedent['fastcgi_ports'] ?? []);
        } catch (\Throwable $e) {
            $bilan['erreurs'][] = $e->getMessage();

            return $this->terminer($bilan);
        }

        if ($http['origine'] === 'systeme') {
            $bilan['avertissements'][] = "Aucun port habituel n'était libre : le système a choisi {$http['port']}. Les postes suivront grâce à la découverte automatique.";
        } elseif ($http['origine'] === 'repli') {
            $bilan['avertissements'][] = "Le port préféré est occupé : le port {$http['port']} sera utilisé. Les postes suivront grâce à la découverte automatique.";
        }

        // 4. Caddyfile : validation par Caddy lui-même
        $contenu = null;
        if ($caddyPresent) {
            $contenu = $this->choisirCaddyfile($generateur, $caddy, $http['port'], $fastcgi, $racinePublique, $etat, $simuler, $bilan);
        }

        // 5. Écriture (Caddyfile d'abord, ports.json en dernier)
        if ($bilan['erreurs'] === [] && $contenu !== null) {
            $nouveau = $this->construireEtat($etat, $http, $fastcgi, $interfaces, $caddy, $phpCgi, $racinePublique, $contenu);

            $bilan['modifie'] = $precedent === null || $this->sansHorodatage($precedent) !== $this->sansHorodatage($nouveau);
            $bilan['etat']    = $nouveau;

            if (!$simuler) {
                try {
                    $etat->assurerDossiers();
                    $resultat = $generateur->installer($contenu, $etat->fichierCaddyfile());
                    $bilan['caddyfile_modifie']    = $resultat['modifie'];
                    $bilan['sauvegarde_caddyfile'] = $resultat['sauvegarde'];
                    $etat->ecrire($nouveau);
                } catch (\Throwable $e) {
                    $bilan['erreurs'][] = 'Écriture impossible : ' . $e->getMessage();
                }
            }
        }

        return $this->terminer($bilan);
    }

    // ------------------------------------------------------------------
    // Caddyfile
    // ------------------------------------------------------------------

    /** Essaie le profil robuste puis le profil minimal ; renvoie le contenu validé (ou null avec une erreur). */
    private function choisirCaddyfile(GenerateurCaddyfile $generateur, string $caddy, int $portHttp, array $fastcgi, string $racine, EtatMoteur $etat, bool $simuler, array &$bilan): ?string
    {
        $journalReel       = $etat->dossierLogs() . DIRECTORY_SEPARATOR . 'caddy.log';
        $journalValidation = sys_get_temp_dir() . DIRECTORY_SEPARATOR . 'cyber-manager-validation-caddy.log';
        $derniereErreur    = '';

        foreach ([true, false] as $robuste) {
            try {
                $contenu = $generateur->generer($portHttp, $fastcgi, $racine, $journalReel, $robuste);
                // En simulation, on valide avec un journal temporaire pour ne rien créer dans ProgramData.
                $aValider = $simuler
                    ? $generateur->generer($portHttp, $fastcgi, $racine, $journalValidation, $robuste)
                    : $contenu;
            } catch (\Throwable $e) {
                $bilan['erreurs'][] = 'Caddyfile impossible à générer : ' . $e->getMessage();

                return null;
            }

            $validation = $generateur->valider($aValider, $caddy);
            if ($simuler) {
                @unlink($journalValidation);
            }

            if ($validation['valide']) {
                $bilan['tests']['caddyfile'] = ['ok' => true, 'profil' => $robuste ? 'robuste' : 'minimal'];
                if (!$robuste) {
                    $bilan['avertissements'][] = 'Caddy a refusé les options de réessai : profil minimal utilisé (' . $derniereErreur . ').';
                }

                return $contenu;
            }

            $derniereErreur = $validation['message'];
        }

        $bilan['tests']['caddyfile'] = ['ok' => false];
        $bilan['erreurs'][] = 'Le Caddyfile est refusé par Caddy : ' . $derniereErreur;

        return null;
    }

    // ------------------------------------------------------------------
    // Tests des programmes du bundle
    // ------------------------------------------------------------------

    private function testerCaddy(string $caddy, array &$bilan): void
    {
        $r = Processus::executer([$caddy, 'version'], 15);

        if ($r['code'] !== 0) {
            $bilan['tests']['caddy'] = ['ok' => false];
            $bilan['erreurs'][] = 'caddy.exe ne démarre pas : ' . $this->extrait($r);

            return;
        }

        $bilan['tests']['caddy'] = ['ok' => true, 'version' => trim(strtok($r['sortie'], "\n") ?: '')];
    }

    private function testerPhpCgi(string $phpCgi, array &$bilan): void
    {
        $v = Processus::executer([$phpCgi, '-v'], 15);
        if ($v['code'] !== 0) {
            $bilan['tests']['php_cgi'] = ['ok' => false];
            $bilan['erreurs'][] = 'php-cgi.exe ne démarre pas : ' . $this->extrait($v);

            return;
        }

        $premiere = trim(strtok($v['sortie'], "\n") ?: '');
        if (!preg_match('/^PHP (\d+\.\d+\.\d+)/', $premiere, $m)) {
            $bilan['tests']['php_cgi'] = ['ok' => false];
            $bilan['erreurs'][] = "Sortie inattendue de php-cgi.exe : {$premiere}";

            return;
        }

        $bilan['tests']['php_cgi'] = ['ok' => true, 'version' => $premiere];

        if (version_compare($m[1], self::PHP_MINIMUM, '<')) {
            $bilan['erreurs'][] = "PHP {$m[1]} est trop ancien : Laravel 12 demande PHP " . self::PHP_MINIMUM . ' ou plus.';
        }
        if (!str_contains($premiere, 'cgi')) {
            $bilan['avertissements'][] = "Ce programme ne semble pas être un php-cgi : {$premiere}";
        }
        if (!str_contains($v['sortie'], 'NTS')) {
            $bilan['avertissements'][] = 'Ce PHP n\'est pas une version « NTS » (Non Thread Safe) : c\'est la version recommandée pour FastCGI.';
        }

        // Extensions
        $m2 = Processus::executer([$phpCgi, '-m'], 15);
        $modules = [];
        foreach (preg_split('/\R/', $m2['sortie']) ?: [] as $ligne) {
            $ligne = strtolower(trim($ligne));
            if ($ligne !== '' && $ligne[0] !== '[') {
                $modules[] = $ligne;
            }
        }
        $manquantes = array_values(array_diff(self::EXTENSIONS_REQUISES, $modules));
        $bilan['tests']['extensions'] = ['ok' => $manquantes === [], 'manquantes' => $manquantes];
        if ($manquantes !== []) {
            $bilan['erreurs'][] = 'Extensions PHP manquantes dans php-cgi : ' . implode(', ', $manquantes) . ' (à activer dans le php.ini du bundle).';
        }

        // Démarrage réel de Laravel : charge vendor/autoload.php (déclenche le contrôle de plateforme de Composer)
        $autoload = base_path('vendor' . DIRECTORY_SEPARATOR . 'autoload.php');
        $script   = tempnam(sys_get_temp_dir(), 'cmboot');
        if ($script === false) {
            $bilan['avertissements'][] = 'Test de démarrage de Laravel ignoré (fichier temporaire impossible).';

            return;
        }

        try {
            file_put_contents($script, '<?php require ' . var_export($autoload, true) . "; echo 'CM_DEMARRAGE_OK';");
            $b = Processus::executer([$phpCgi, '-q', $script], 30);
            $ok = str_contains($b['sortie'], 'CM_DEMARRAGE_OK');

            $bilan['tests']['demarrage_laravel'] = ['ok' => $ok];
            if (!$ok) {
                $bilan['erreurs'][] = 'Laravel ne démarre pas avec ce php-cgi : ' . $this->extrait($b);
            }
        } finally {
            @unlink($script);
        }
    }

    // ------------------------------------------------------------------
    // État (ports.json)
    // ------------------------------------------------------------------

    private function construireEtat(EtatMoteur $etat, array $http, array $fastcgi, array $interfaces, string $caddy, string $phpCgi, string $racinePublique, string $contenu): array
    {
        $workers = [];
        foreach ($fastcgi as $port) {
            $workers[] = ['port' => $port, 'commande' => [$phpCgi, '-b', '127.0.0.1:' . $port]];
        }

        $udp  = ParametresResilience::portDecouverteUdp();
        $urls = [$this->url('127.0.0.1', $http['port'])];
        foreach ($interfaces as $ip) {
            $urls[] = $this->url($ip, $http['port']);
        }

        return [
            'version'          => EtatMoteur::VERSION,
            'genere_le'        => gmdate('Y-m-d\TH:i:s\Z'),
            'http_port'        => $http['port'],
            'http_origine'     => $http['origine'],
            'fastcgi_ports'    => $fastcgi,
            'workers'          => $workers,
            'env_workers'      => ['PHP_FCGI_MAX_REQUESTS' => self::FCGI_MAX_REQUESTS],
            'caddy'            => $caddy,
            'php_cgi'          => $phpCgi,
            'php_cli'          => $etat->cheminPhpCli(),
            'caddyfile'        => $etat->fichierCaddyfile(),
            'caddyfile_sha256' => hash('sha256', $contenu),
            'racine_publique'  => $racinePublique,
            'dossier_logs'     => $etat->dossierLogs(),
            'interfaces'       => $interfaces,
            'urls'             => $urls,
            'decouverte'       => [
                'udp_port'  => $udp,
                'http_port' => $http['port'],
                'arguments' => ['artisan', 'cyber:decouverte', '--http-port=' . $http['port'], '--udp-port=' . $udp],
            ],
        ];
    }

    private function sansHorodatage(array $etat): string
    {
        unset($etat['genere_le']);

        return (string) json_encode($etat, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    }

    private function url(string $hote, int $port): string
    {
        return 'http://' . $hote . ($port === 80 ? '' : ':' . $port);
    }

    // ------------------------------------------------------------------
    // Sorties
    // ------------------------------------------------------------------

    private function terminer(array $bilan): int
    {
        $bilan['ok'] = $bilan['erreurs'] === [];

        if ($this->option('json')) {
            $this->line(json_encode($bilan, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES | JSON_PARTIAL_OUTPUT_ON_ERROR));

            return $bilan['ok'] ? self::SUCCESS : self::FAILURE;
        }

        $this->line($bilan['simulation'] ? 'Préparation du moteur — SIMULATION (rien n\'est écrit)' : 'Préparation du moteur');

        foreach (['caddy' => 'Caddy', 'php_cgi' => 'php-cgi'] as $cle => $nom) {
            if (isset($bilan['tests'][$cle])) {
                $t = $bilan['tests'][$cle];
                $this->line(($t['ok'] ? '  ✓ ' : '  ✗ ') . $nom . (isset($t['version']) ? ' : ' . $t['version'] : ''));
            }
        }
        if (isset($bilan['tests']['extensions'])) {
            $this->line(($bilan['tests']['extensions']['ok'] ? '  ✓ ' : '  ✗ ') . 'Extensions PHP requises');
        }
        if (isset($bilan['tests']['demarrage_laravel'])) {
            $this->line(($bilan['tests']['demarrage_laravel']['ok'] ? '  ✓ ' : '  ✗ ') . 'Démarrage de Laravel avec php-cgi');
        }
        if (isset($bilan['tests']['caddyfile'])) {
            $t = $bilan['tests']['caddyfile'];
            $this->line(($t['ok'] ? '  ✓ ' : '  ✗ ') . 'Caddyfile validé par Caddy' . (isset($t['profil']) ? " (profil {$t['profil']})" : ''));
        }

        if ($bilan['etat'] !== null) {
            $e = $bilan['etat'];
            $this->newLine();
            $this->table(['Réglage', 'Valeur'], [
                ['Port HTTP', $e['http_port'] . ' (' . $e['http_origine'] . ')'],
                ['Workers FastCGI', count($e['fastcgi_ports']) . ' : ' . implode(', ', $e['fastcgi_ports'])],
                ['Adresses', implode("\n", $e['urls'])],
                ['Découverte UDP', (string) $e['decouverte']['udp_port']],
                ['Caddyfile', $e['caddyfile']],
                ['État', $this->option('simuler') ? 'non écrit (simulation)' : ($bilan['modifie'] ? 'mis à jour' : 'inchangé')],
            ]);
        }

        foreach ($bilan['avertissements'] as $a) {
            $this->warn("Attention : {$a}");
        }
        foreach ($bilan['erreurs'] as $erreur) {
            $this->error($erreur);
        }

        return $bilan['ok'] ? self::SUCCESS : self::FAILURE;
    }

    private function optionEntiere(string $nom, int $min, int $max): ?int
    {
        $valeur = $this->option($nom);
        if ($valeur === null || $valeur === '') {
            return null;
        }

        if (!ctype_digit((string) $valeur) || (int) $valeur < $min || (int) $valeur > $max) {
            throw new RuntimeException("--{$nom} doit être un entier entre {$min} et {$max}.");
        }

        return (int) $valeur;
    }

    private function extrait(array $r): string
    {
        if ($r['delai_depasse']) {
            return 'délai dépassé';
        }

        return mb_substr(trim($r['erreur'] !== '' ? $r['erreur'] : $r['sortie']), 0, 300);
    }
}