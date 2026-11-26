<?php

use Illuminate\Foundation\Application;
use Illuminate\Foundation\Configuration\Exceptions;
use Illuminate\Foundation\Configuration\Middleware;

$app = Application::configure(basePath: dirname(__DIR__))
    ->withRouting(
        web: __DIR__.'/../routes/web.php',
        api: __DIR__.'/../routes/api.php',
        commands: __DIR__.'/../routes/console.php',
        health: '/up',
    )
    ->withMiddleware(function (Middleware $middleware): void {
        // Exclure les routes agent de la vérification CSRF
        $middleware->validateCsrfTokens(except: [
            'api/agent/*',
        ]);

        // Alias des middlewares
        $middleware->alias([
            'role' => \App\Http\Middleware\RoleMiddleware::class,
            'verified.email' => \App\Http\Middleware\ForceEmailVerification::class,
        ]);
    })
    ->withExceptions(function (Exceptions $exceptions): void {
        //
    })->create();

/*
|--------------------------------------------------------------------------
| Support des chemins absolus Windows (C:\, D:\...) pour les caches
|--------------------------------------------------------------------------
| Par défaut, Laravel ne considère comme absolus que les chemins débutant
| par '/' ou '\'. Sous Windows, un chemin absolu comme C:\... est alors pris
| pour un chemin relatif et préfixé par basePath(), provoquant une erreur.
*/
if (DIRECTORY_SEPARATOR === '\\') {
    foreach (range('A', 'Z') as $lettre) {
        $app->addAbsoluteCachePathPrefix($lettre . ':');
        $app->addAbsoluteCachePathPrefix(strtolower($lettre) . ':');
    }
}

/*
|--------------------------------------------------------------------------
| Mode « serveur Windows » (service Cyber Manager)
|--------------------------------------------------------------------------
| Le code est en lecture seule dans Program Files : le .env et le dossier storage
| vivent dans %ProgramData%\CyberManager. Le service Windows définit CYBER_DONNEES_DIR
| pour tous les processus PHP (php-cgi, artisan).
*/
$donneesServeur = getenv('CYBER_DONNEES_DIR');

if (is_string($donneesServeur) && $donneesServeur !== '') {
    $donneesServeur = rtrim($donneesServeur, '\\/');
    $dossierConfig  = $donneesServeur . DIRECTORY_SEPARATOR . 'config';

    if (is_file($dossierConfig . DIRECTORY_SEPARATOR . '.env')) {
        $app->useEnvironmentPath($dossierConfig);
    }

    $stockage = getenv('APP_STORAGE_PATH');
    $app->useStoragePath(
        (is_string($stockage) && $stockage !== '')
            ? $stockage
            : $donneesServeur . DIRECTORY_SEPARATOR . 'storage'
    );
}

return $app;