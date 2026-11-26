<?php

use Illuminate\Foundation\Inspiring;
use Illuminate\Support\Facades\Artisan;
use Illuminate\Support\Facades\Schedule;

Artisan::command('inspire', function () {
    $this->comment(Inspiring::quote());
})->purpose('Display an inspiring quote');

// Pouls du serveur : dernier signe de vie, base de la détection de coupure (résilience)
Schedule::command('serveur:pulse')->everyTenSeconds();

// Synchronise les sessions Wi-Fi depuis le MikroTik, toutes les 5 secondes,
// même si aucune page n'est ouverte. Le verrou est dans WifiSessionService.
Schedule::command('wifi:sync-sessions')
    ->everyFiveSeconds()
    ->withoutOverlapping(2);

// Expire les vouchers dépassés et réapprovisionne le pool de temporaires
Schedule::command('vouchers:reapprovisionner')
    ->everyFiveMinutes()
    ->withoutOverlapping();

// Maintenance des postes : expiration des forfaits, gel des postes injoignables, abandon des sessions figées
Schedule::command('postes:maintenance')->everyTenSeconds();

// Accès Internet des postes : réconciliation avec le routeur
Schedule::command('postes:maintenance --reseau')->everyThirtySeconds();

// RETIRÉ : « sessions:expire » (ancien moteur SessionService). Il contredisait le nouveau moteur
// (ignore les temps de coupure, état « expiree » inconnu, remise à zéro de duree_consommee).