<?php

namespace App\Console\Commands;

use Illuminate\Console\Command;
use Illuminate\Database\Connection;
use Illuminate\Support\Facades\DB;
use RuntimeException;

/**
 * Reprise d'historique : copie une ancienne base MariaDB/MySQL (XAMPP) vers cyber.sqlite.
 *
 *  - la base source est DÉTECTÉE (celle qui contient « cyber_sessions ») sauf si --database est donné ;
 *  - tout se passe dans UNE transaction : une erreur ou un écart de comptage annule tout ;
 *  - une sauvegarde de cyber.sqlite est faite avant (VACUUM INTO) ;
 *  - les lignes « serveur.* » de cyber_settings ne sont jamais reprises (elles fausseraient la détection de coupure).
 */
class ServeurImporterMysql extends Command
{
    private const SOURCE          = 'import_mysql';
    private const TABLE_SIGNATURE = 'cyber_sessions';
    private const LOT_MAX         = 500;
    private const VARIABLES_MAX   = 30000;

    /** Tables techniques de Laravel : sans intérêt à reprendre (les utilisateurs se reconnecteront). */
    private const TABLES_IGNOREES = [
        'migrations', 'sessions', 'cache', 'cache_locks', 'jobs', 'job_batches', 'failed_jobs',
    ];

    protected $signature = 'serveur:importer-mysql
        {--host= : Hote MariaDB/MySQL (defaut : reglage du .env)}
        {--port= : Port MariaDB/MySQL}
        {--database= : Nom de l ancienne base (defaut : detection automatique)}
        {--username= : Utilisateur MariaDB/MySQL}
        {--password= : Mot de passe MariaDB/MySQL}
        {--simuler : Lit et ecrit tout, puis annule (rien n est modifie)}
        {--remplacer : Vide les tables concernees de cyber.sqlite avant l import}
        {--json : Resultat au format JSON (pour l installateur)}';

    protected $description = 'Reprend l\'historique d\'une ancienne base MariaDB/MySQL (XAMPP) dans cyber.sqlite';

    public function handle(): int
    {
        $cible = DB::connection();
        if ($cible->getDriverName() !== 'sqlite') {
            return $this->echec('La base de destination doit être SQLite (DB_CONNECTION=sqlite).');
        }

        $bilan = [
            'simulation'  => (bool) $this->option('simuler'),
            'base_source' => null,
            'sauvegarde'  => null,
            'tables'      => [],
            'ignorees'    => [],
            'sequences'   => [],
            'orphelins'   => [],
        ];

        try {
            $source = $this->ouvrirSource($bilan);
            $plan   = $this->preparerPlan($source, $cible, $bilan);
            $this->verifierCibleVide($cible, $plan);

            if (!$bilan['simulation']) {
                $bilan['sauvegarde'] = $this->sauvegarder($cible);
            }

            $this->importer($source, $cible, $plan, $bilan);
        } catch (\Throwable $e) {
            return $this->echec($e->getMessage(), $bilan);
        }

        return $this->rapport($bilan);
    }

    // ------------------------------------------------------------------
    // Connexion à l'ancienne base
    // ------------------------------------------------------------------

    private function ouvrirSource(array &$bilan): Connection
    {
        $config = config('database.connections.mysql');
        foreach (['host', 'port', 'username', 'password'] as $option) {
            $valeur = $this->option($option);
            if ($valeur !== null) {
                $config[$option] = $valeur;
            }
        }

        $connexion = $this->connecter($config, null);

        $base = $this->option('database');
        if (!$base) {
            $lignes = $connexion->select(
                "SELECT DISTINCT table_schema AS base FROM information_schema.tables
                 WHERE table_name = ? AND table_schema NOT IN ('information_schema', 'mysql', 'performance_schema', 'sys')",
                [self::TABLE_SIGNATURE]
            );
            $bases = array_map(fn ($l) => (string) $l->base, $lignes);

            if (count($bases) === 0) {
                throw new RuntimeException('Aucune base contenant « ' . self::TABLE_SIGNATURE . ' » n\'a été trouvée. Indiquez-la avec --database=NOM.');
            }
            if (count($bases) > 1) {
                throw new RuntimeException('Plusieurs bases possibles : ' . implode(', ', $bases) . '. Indiquez la bonne avec --database=NOM.');
            }
            $base = $bases[0];
        }

        $bilan['base_source'] = $base;

        return $this->connecter($config, $base);
    }

    private function connecter(array $config, ?string $base): Connection
    {
        $config['database'] = $base;
        config(['database.connections.' . self::SOURCE => $config]);
        DB::purge(self::SOURCE);

        $connexion = DB::connection(self::SOURCE);
        try {
            $connexion->getPdo();
        } catch (\Throwable $e) {
            throw new RuntimeException('Impossible de joindre MariaDB/MySQL (' . ($config['host'] ?? '?') . ':' . ($config['port'] ?? '?') . ') : ' . $e->getMessage());
        }

        return $connexion;
    }

    // ------------------------------------------------------------------
    // Plan : quelles tables, quelles colonnes, dans quel ordre
    // ------------------------------------------------------------------

    /** @return array<string, array{colonnes:string[], absentes_cible:string[], pk:?string, pk_entier:bool}> */
    private function preparerPlan(Connection $source, Connection $cible, array &$bilan): array
    {
        $tablesSource = array_map(
            fn ($l) => (string) $l->nom,
            $source->select("SELECT table_name AS nom FROM information_schema.tables WHERE table_schema = ? AND table_type = 'BASE TABLE'", [$bilan['base_source']])
        );
        $tablesCible = array_map(
            fn ($l) => (string) $l->name,
            $cible->select("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'")
        );

        if (!in_array(self::TABLE_SIGNATURE, $tablesCible, true)) {
            throw new RuntimeException('cyber.sqlite n\'est pas encore migrée. Lancez d\'abord : php artisan migrate --force');
        }

        $communes = [];
        foreach ($tablesSource as $table) {
            if (in_array($table, self::TABLES_IGNOREES, true)) {
                $bilan['ignorees'][$table] = 'table technique';
            } elseif (!in_array($table, $tablesCible, true)) {
                $bilan['ignorees'][$table] = 'inconnue de cyber.sqlite';
            } else {
                $communes[] = $table;
            }
        }

        $plan      = [];
        $problemes = [];

        foreach ($this->ordonner($cible, $communes) as $table) {
            $colsSource = array_map(fn ($l) => (string) $l->Field, $source->select('SHOW COLUMNS FROM ' . $this->identMysql($table)));
            $infos      = $cible->select('PRAGMA table_info(' . $this->identSqlite($table) . ')');

            $colonnes = [];
            $pks      = [];
            foreach ($infos as $info) {
                $nom = (string) $info->name;
                if ((int) $info->pk > 0) {
                    $pks[] = $info;
                }
                if (in_array($nom, $colsSource, true)) {
                    $colonnes[] = $nom;
                } elseif ((int) $info->notnull === 1 && $info->dflt_value === null && (int) $info->pk === 0) {
                    $problemes[] = "{$table}.{$nom} est obligatoire dans cyber.sqlite mais absente de l'ancienne base.";
                }
            }

            $nomsCible = array_map(fn ($i) => (string) $i->name, $infos);

            $plan[$table] = [
                'colonnes'       => $colonnes,
                'absentes_cible' => array_values(array_diff($colsSource, $nomsCible)),
                'pk'             => count($pks) === 1 ? (string) $pks[0]->name : null,
                'pk_entier'      => count($pks) === 1 && strtoupper((string) $pks[0]->type) === 'INTEGER',
            ];
        }

        if ($problemes !== []) {
            throw new RuntimeException("Import impossible :\n - " . implode("\n - ", $problemes));
        }

        return $plan;
    }

    /** Parents avant enfants (graphe des clés étrangères de cyber.sqlite) ; les cycles sont cassés arbitrairement. */
    private function ordonner(Connection $cible, array $tables): array
    {
        $restant = [];
        foreach ($tables as $t) {
            $restant[$t] = [];
            foreach ($cible->select('PRAGMA foreign_key_list(' . $this->identSqlite($t) . ')') as $fk) {
                $parent = (string) $fk->table;
                if ($parent !== $t && in_array($parent, $tables, true)) {
                    $restant[$t][$parent] = true;
                }
            }
        }

        $ordre = [];
        while ($restant !== []) {
            $prets = [];
            foreach ($restant as $t => $parents) {
                if (array_diff(array_keys($parents), $ordre) === []) {
                    $prets[] = $t;
                }
            }
            if ($prets === []) {
                $prets = [array_key_first($restant)];
            }
            sort($prets);
            foreach ($prets as $t) {
                $ordre[] = $t;
                unset($restant[$t]);
            }
        }

        return $ordre;
    }

    private function verifierCibleVide(Connection $cible, array $plan): void
    {
        if ($this->option('remplacer')) {
            return;
        }

        $pleines = [];
        foreach (array_keys($plan) as $table) {
            $n = $cible->table($table)->count();
            if ($n > 0) {
                $pleines[] = "{$table} ({$n} ligne(s))";
            }
        }

        if ($pleines !== []) {
            throw new RuntimeException('cyber.sqlite contient déjà des données : ' . implode(', ', $pleines) . '. Ajoutez --remplacer pour les écraser (une sauvegarde est faite avant).');
        }
    }

    private function sauvegarder(Connection $cible): ?string
    {
        $fichier = (string) $cible->getDatabaseName();
        if ($fichier === ':memory:' || !is_file($fichier)) {
            return null;
        }

        $destination = $fichier . '.avant-import-' . date('Ymd-His') . '.bak';
        $cible->statement("VACUUM INTO '" . str_replace("'", "''", $destination) . "'");

        return $destination;
    }

    // ------------------------------------------------------------------
    // Copie
    // ------------------------------------------------------------------

    private function importer(Connection $source, Connection $cible, array $plan, array &$bilan): void
    {
        $fkAvant = (int) ($cible->selectOne('PRAGMA foreign_keys')->foreign_keys ?? 1);

        // Ne peut pas être changé à l'intérieur d'une transaction
        $cible->statement('PRAGMA foreign_keys = OFF');
        $cible->beginTransaction();

        try {
            if ($this->option('remplacer')) {
                foreach (array_reverse(array_keys($plan)) as $table) {
                    $cible->table($table)->delete();
                }
            }

            foreach ($plan as $table => $info) {
                $bilan['tables'][$table] = $this->copier($source, $cible, $table, $info['colonnes']);
            }

            $bilan['sequences'] = $this->reinitialiserSequences($cible, $plan);
            $bilan['orphelins'] = $this->orphelins($cible);

            if ($bilan['simulation']) {
                $cible->rollBack();
            } else {
                $cible->commit();
            }
        } catch (\Throwable $e) {
            if ($cible->transactionLevel() > 0) {
                $cible->rollBack();
            }
            throw $e;
        } finally {
            $cible->statement('PRAGMA foreign_keys = ' . $fkAvant);
        }
    }

    /** @return array{lues:int, importees:int, ignorees:int} */
    private function copier(Connection $source, Connection $cible, string $table, array $colonnes): array
    {
        $res = ['lues' => 0, 'importees' => 0, 'ignorees' => 0];
        if ($colonnes === []) {
            return $res;
        }

        $liste  = implode(', ', array_map(fn ($c) => $this->identMysql($c), $colonnes));
        $taille = max(1, min(self::LOT_MAX, intdiv(self::VARIABLES_MAX, count($colonnes))));
        $lot    = [];

        foreach ($source->cursor("SELECT {$liste} FROM " . $this->identMysql($table)) as $ligne) {
            $res['lues']++;
            $ligne = (array) $ligne;

            if (!$this->accepte($table, $ligne)) {
                $res['ignorees']++;
                continue;
            }

            foreach ($ligne as $cle => $valeur) {
                $ligne[$cle] = $this->nettoyer($valeur);
            }

            $lot[] = $ligne;
            if (count($lot) >= $taille) {
                $cible->table($table)->insert($lot);
                $res['importees'] += count($lot);
                $lot = [];
            }
        }

        if ($lot !== []) {
            $cible->table($table)->insert($lot);
            $res['importees'] += count($lot);
        }

        $presentes = $cible->table($table)->count();
        if ($presentes !== $res['importees']) {
            throw new RuntimeException("Écart de comptage sur {$table} : {$res['importees']} écrites, {$presentes} présentes. Import annulé.");
        }

        return $res;
    }

    private function accepte(string $table, array $ligne): bool
    {
        if ($table === 'cyber_settings' && isset($ligne['cle']) && str_starts_with((string) $ligne['cle'], 'serveur.')) {
            return false;
        }

        return true;
    }

    private function nettoyer(mixed $valeur): mixed
    {
        // MySQL tolère « 0000-00-00 » ; SQLite le garderait tel quel et Carbon planterait à la lecture.
        if (is_string($valeur) && str_starts_with($valeur, '0000-00-00')) {
            return null;
        }

        return $valeur;
    }

    /** @return array<string,int> table => dernière valeur de la séquence */
    private function reinitialiserSequences(Connection $cible, array $plan): array
    {
        $existe = $cible->selectOne("SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'sqlite_sequence'");
        if ($existe === null) {
            return [];
        }

        $res = [];
        foreach ($plan as $table => $info) {
            if (!$info['pk_entier'] || $info['pk'] === null) {
                continue;
            }

            $def = $cible->selectOne("SELECT sql FROM sqlite_master WHERE type = 'table' AND name = ?", [$table]);
            if ($def === null || stripos((string) $def->sql, 'AUTOINCREMENT') === false) {
                continue;
            }

            $max = (int) $cible->table($table)->max($info['pk']);
            $cible->delete('DELETE FROM sqlite_sequence WHERE name = ?', [$table]);
            if ($max > 0) {
                $cible->insert('INSERT INTO sqlite_sequence (name, seq) VALUES (?, ?)', [$table, $max]);
            }
            $res[$table] = $max;
        }

        return $res;
    }

    /** @return string[] lignes orphelines, regroupées (avertissement, jamais bloquant) */
    private function orphelins(Connection $cible): array
    {
        $groupes = [];
        foreach ($cible->select('PRAGMA foreign_key_check') as $l) {
            $cle = "{$l->table} → {$l->parent}";
            $groupes[$cle] = ($groupes[$cle] ?? 0) + 1;
        }

        $res = [];
        foreach ($groupes as $cle => $n) {
            $res[] = "{$cle} : {$n} ligne(s) sans parent";
        }

        return $res;
    }

    // ------------------------------------------------------------------
    // Sorties
    // ------------------------------------------------------------------

    private function rapport(array $bilan): int
    {
        if ($this->option('json')) {
            $this->line(json_encode(['ok' => true] + $bilan, JSON_UNESCAPED_UNICODE | JSON_PARTIAL_OUTPUT_ON_ERROR));

            return self::SUCCESS;
        }

        $lignes = [];
        foreach ($bilan['tables'] as $table => $r) {
            $lignes[] = [$table, $r['lues'], $r['importees'], $r['ignorees']];
        }
        $this->table(['Table', 'Lues', 'Importées', 'Ignorées'], $lignes);

        foreach ($bilan['ignorees'] as $table => $raison) {
            $this->line("  - {$table} non reprise ({$raison})");
        }
        foreach ($bilan['orphelins'] as $o) {
            $this->warn("Attention : {$o}");
        }

        if ($bilan['simulation']) {
            $this->info("Simulation terminée sur la base « {$bilan['base_source']} » : rien n'a été modifié.");
        } else {
            $this->info("Import terminé depuis « {$bilan['base_source']} ».");
            if ($bilan['sauvegarde']) {
                $this->line('Sauvegarde de l\'ancien cyber.sqlite : ' . $bilan['sauvegarde']);
            }
            $this->line('Les sessions « en cours » reprises seront figées au dernier signe de vie de leur poste par la maintenance, sans facturer le temps écoulé depuis.');
        }

        return self::SUCCESS;
    }

    private function echec(string $message, array $bilan = []): int
    {
        if ($this->option('json')) {
            $this->line(json_encode(['ok' => false, 'erreur' => $message] + $bilan, JSON_UNESCAPED_UNICODE | JSON_PARTIAL_OUTPUT_ON_ERROR));
        } else {
            $this->error($message);
        }

        return self::FAILURE;
    }

    private function identMysql(string $nom): string
    {
        return '`' . str_replace('`', '``', $nom) . '`';
    }

    private function identSqlite(string $nom): string
    {
        return '"' . str_replace('"', '""', $nom) . '"';
    }
}