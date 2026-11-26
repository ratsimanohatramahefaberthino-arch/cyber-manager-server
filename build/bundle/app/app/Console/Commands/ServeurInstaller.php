<?php

namespace App\Console\Commands;

use App\Models\CyberSetting;
use App\Models\User;
use App\Services\CodeInstallation;
use App\Services\ParametresMoteur;
use Illuminate\Console\Command;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Schema;
use InvalidArgumentException;

/**
 * Premier réglage du serveur, appelé par l'installateur (jamais à la main par le gérant).
 *
 * La demande (JSON UTF-8) arrive par l'entrée standard (--stdin) ou par un fichier (--demande) :
 *   { "nom_cyber", "admin_nom", "admin_email", "mot_de_passe", "port_http" }
 * Le mot de passe n'est JAMAIS passé en argument (visible dans la liste des processus) ni journalisé.
 *
 * Règle d'or : ne jamais écraser des données. L'état de la base décide de ce qui est permis :
 *  - absente    : fichier vide ou inexistant  → création, migrations, administrateur, réglages ;
 *  - incomplete : base sans administrateur (installation interrompue) → idem, SI les infos de l'administrateur
 *                 sont fournies (sauvegarde préalable) ; sinon on ne touche à rien ;
 *  - existante  : un administrateur existe → on ne migre pas (le service sauvegarde puis migre au démarrage),
 *                 on ne touche ni aux comptes ni au nom du cyber. Seul le port HTTP choisi peut être enregistré.
 */
class ServeurInstaller extends Command
{
    protected $signature = 'serveur:installer
        {--stdin : Lit la demande JSON sur l entree standard (utilise par l installateur)}
        {--demande= : Lit la demande JSON dans ce fichier (tests, depannage)}
        {--json : Resultat au format JSON}';

    protected $description = 'Premier réglage : base, administrateur, nom du cyber, port HTTP (sans jamais écraser de données)';

    public function handle(): int
    {
        $bilan = [
            'ok'                => false,
            'base'              => null,
            'admin'             => 'absent',
            'port_http'         => null,
            'code_installation' => null,
            'erreurs'           => [],
            'avertissements'    => [],
        ];

        try {
            $demande = $this->lireDemande();
        } catch (InvalidArgumentException $e) {
            $bilan['erreurs'][] = $e->getMessage();

            return $this->terminer($bilan);
        }

        $admin = $this->extraireAdmin($demande, $bilan);
        $nomCyber = $this->texte($demande['nom_cyber'] ?? null);
        $port = $demande['port_http'] ?? null;

        if ($nomCyber !== '' && mb_strlen($nomCyber) > 80) {
            $bilan['erreurs'][] = 'Le nom du cyber ne doit pas dépasser 80 caractères.';
        }
        if ($port !== null && (!is_int($port) || $port < 1 || $port > 65535)) {
            $bilan['erreurs'][] = 'Le port HTTP doit être un entier entre 1 et 65535.';
        }

        $etat = $this->etatBase();
        $bilan['base'] = $etat;

        if ($etat === 'absente' && $admin === null && $bilan['erreurs'] === []) {
            $bilan['erreurs'][] = "Les informations de l'administrateur sont obligatoires pour une première installation.";
        }

        if ($bilan['erreurs'] !== []) {
            return $this->terminer($bilan);
        }

        $agir = $etat === 'absente' || ($etat === 'incomplete' && $admin !== null);

        try {
            if ($agir) {
                $this->initialiserBase($etat, $admin, $nomCyber, $bilan);
            } elseif ($etat === 'existante') {
                $bilan['admin'] = 'conserve';
                if ($admin !== null) {
                    $bilan['avertissements'][] = "La base contient déjà un administrateur : les informations d'administrateur fournies ont été ignorées.";
                }
            } else {
                $bilan['avertissements'][] = "La base est incomplète et aucune information d'administrateur n'a été fournie : rien n'a été modifié.";
            }

            if ($bilan['erreurs'] === []) {
                $this->enregistrerPort($port, $bilan);
                $this->lireCodeInstallation($bilan);
            }
        } catch (\Throwable $e) {
            $bilan['erreurs'][] = 'Erreur pendant la mise en place : ' . $e->getMessage();
        }

        return $this->terminer($bilan);
    }

    // ------------------------------------------------------------------
    // État de la base
    // ------------------------------------------------------------------

    private function etatBase(): string
    {
        if (DB::connection()->getDriverName() === 'sqlite') {
            $fichier = (string) config('database.connections.sqlite.database');

            if ($fichier !== '' && $fichier !== ':memory:' && (!is_file($fichier) || filesize($fichier) === 0)) {
                return 'absente';
            }
        }

        try {
            if (!Schema::hasTable('users') || !Schema::hasColumn('users', 'role')) {
                return 'incomplete';
            }

            return User::query()->where('role', User::ROLE_ADMIN)->exists() ? 'existante' : 'incomplete';
        } catch (\Throwable) {
            return 'incomplete';
        }
    }

    private function initialiserBase(string $etat, ?array $admin, string $nomCyber, array &$bilan): void
    {
        if ($etat === 'incomplete') {
            // Base déjà peuplée mais sans administrateur : par prudence, une sauvegarde d'abord (meilleur effort)
            try {
                if ($this->getApplication()?->has('serveur:sauvegarder')) {
                    $this->callSilently('serveur:sauvegarder');
                }
            } catch (\Throwable) {
                // Sauvegarde facultative
            }
        }

        if ($etat === 'absente') {
            $this->creerFichierBase();
        }

        if ($this->callSilently('migrate', ['--force' => true]) !== 0) {
            $bilan['erreurs'][] = 'Les migrations de la base de données ont échoué.';

            return;
        }

        $bilan['base'] = $etat === 'absente' ? 'creee' : 'completee';

        if ($admin !== null) {
            $bilan['admin'] = $this->creerAdmin($admin, $bilan);
        }

        if ($bilan['erreurs'] !== []) {
            return;
        }

        if ($nomCyber !== '') {
            CyberSetting::definir('nom_cyber', $nomCyber);
        }
        if ($admin !== null) {
            CyberSetting::definir('nom_admin', $admin['nom']);
        }
    }

    /** SQLite refuse de migrer un fichier absent en mode non interactif : on crée le fichier vide (jamais s'il existe). */
    private function creerFichierBase(): void
    {
        $fichier = (string) config('database.connections.sqlite.database');
        if ($fichier === '' || $fichier === ':memory:') {
            return;
        }

        $dossier = dirname($fichier);
        if (!is_dir($dossier) && !@mkdir($dossier, 0775, true) && !is_dir($dossier)) {
            throw new \RuntimeException("Impossible de créer le dossier de la base : {$dossier}");
        }

        if (!is_file($fichier) && file_put_contents($fichier, '') === false) {
            throw new \RuntimeException("Impossible de créer la base : {$fichier}");
        }

        DB::purge();
    }

    // ------------------------------------------------------------------
    // Administrateur
    // ------------------------------------------------------------------

    private function creerAdmin(array $admin, array &$bilan): string
    {
        if (User::query()->where('role', User::ROLE_ADMIN)->exists()) {
            return 'conserve';
        }

        if (User::query()->where('email', $admin['email'])->exists()) {
            $bilan['erreurs'][] = 'Cette adresse e-mail est déjà utilisée par un compte non administrateur.';

            return 'absent';
        }

        // role et email_verified_at ne sont pas « fillable » : forceFill est voulu (jamais depuis une saisie web).
        // Le mot de passe est haché par le cast « hashed » du modèle.
        $utilisateur = new User();
        $utilisateur->forceFill([
            'name'              => $admin['nom'],
            'email'             => $admin['email'],
            'password'          => $admin['mot_de_passe'],
            'role'              => User::ROLE_ADMIN,
            'email_verified_at' => now(),
        ])->save();

        return 'cree';
    }

    /** @return array{nom:string,email:string,mot_de_passe:string}|null */
    private function extraireAdmin(array $demande, array &$bilan): ?array
    {
        $nom   = $this->texte($demande['admin_nom'] ?? null);
        $email = $this->texte($demande['admin_email'] ?? null);
        $mdp   = is_string($demande['mot_de_passe'] ?? null) ? $demande['mot_de_passe'] : '';

        $presents = count(array_filter([$nom, $email, $mdp], fn ($v) => $v !== ''));

        if ($presents === 0) {
            return null;
        }
        if ($presents < 3) {
            $bilan['erreurs'][] = "Les informations de l'administrateur sont incomplètes (nom, e-mail et mot de passe sont tous nécessaires).";

            return null;
        }

        if (mb_strlen($nom) < 2 || mb_strlen($nom) > 120) {
            $bilan['erreurs'][] = "Le nom de l'administrateur doit comporter de 2 à 120 caractères.";
        }
        if (mb_strlen($email) > 190 || filter_var($email, FILTER_VALIDATE_EMAIL) === false) {
            $bilan['erreurs'][] = "L'adresse e-mail de l'administrateur n'est pas valide.";
        }
        if (mb_strlen($mdp) < 8 || mb_strlen($mdp) > 128) {
            $bilan['erreurs'][] = 'Le mot de passe doit comporter de 8 à 128 caractères.';
        }

        return ['nom' => $nom, 'email' => mb_strtolower($email), 'mot_de_passe' => $mdp];
    }

    // ------------------------------------------------------------------
    // Port, code d'installation
    // ------------------------------------------------------------------

    private function enregistrerPort(mixed $port, array &$bilan): void
    {
        if ($port === null) {
            return;
        }

        try {
            if (!Schema::hasTable('cyber_settings')) {
                $bilan['avertissements'][] = "Le port HTTP n'a pas pu être enregistré (réglages absents).";

                return;
            }

            ParametresMoteur::definir('port_http_prefere', (string) $port);
            $bilan['port_http'] = (int) $port;
        } catch (\Throwable $e) {
            $bilan['avertissements'][] = "Le port HTTP n'a pas pu être enregistré : " . $e->getMessage();
        }
    }

    private function lireCodeInstallation(array &$bilan): void
    {
        try {
            if (Schema::hasTable('cyber_settings')) {
                $bilan['code_installation'] = CodeInstallation::obtenir();
            }
        } catch (\Throwable $e) {
            $bilan['avertissements'][] = "Le code d'installation des postes n'a pas pu être lu : " . $e->getMessage();
        }
    }

    // ------------------------------------------------------------------
    // Entrées / sorties
    // ------------------------------------------------------------------

    /** @return array<string,mixed> */
    private function lireDemande(): array
    {
        $fichier = (string) $this->option('demande');

        if ($fichier !== '') {
            if (!is_file($fichier)) {
                throw new InvalidArgumentException("Fichier de demande introuvable : {$fichier}");
            }
            $brut = (string) file_get_contents($fichier);
        } elseif ($this->option('stdin')) {
            $brut = (string) stream_get_contents(STDIN);
        } else {
            throw new InvalidArgumentException('Indiquez --stdin ou --demande=fichier.');
        }

        $json = json_decode(ltrim($brut, "\xEF\xBB\xBF"), true);

        if (!is_array($json)) {
            throw new InvalidArgumentException('La demande est vide ou n\'est pas un JSON valide.');
        }

        return $json;
    }

    private function texte(mixed $valeur): string
    {
        return is_string($valeur) ? trim($valeur) : '';
    }

    private function terminer(array $bilan): int
    {
        $bilan['ok'] = $bilan['erreurs'] === [];

        if ($this->option('json')) {
            $this->line(json_encode($bilan, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES | JSON_PARTIAL_OUTPUT_ON_ERROR));

            return $bilan['ok'] ? self::SUCCESS : self::FAILURE;
        }

        $this->line("Base : {$bilan['base']} · administrateur : {$bilan['admin']}");
        foreach ($bilan['avertissements'] as $a) {
            $this->warn("Attention : {$a}");
        }
        foreach ($bilan['erreurs'] as $e) {
            $this->error($e);
        }

        return $bilan['ok'] ? self::SUCCESS : self::FAILURE;
    }
}