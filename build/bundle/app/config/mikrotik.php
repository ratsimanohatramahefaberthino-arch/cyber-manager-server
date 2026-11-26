<?php

return [

    /*
    |--------------------------------------------------------------------------
    | Configuration MikroTik
    |--------------------------------------------------------------------------
    |
    | Aucune adresse IP en dur (Règle 1). Host est vide par défaut :
    | Cyber Manager fonctionne en réseau local autonome si aucun routeur n'est configuré.
    |
    */

    'host'     => env('MIKROTIK_HOST', ''),
    'port'     => (int) env('MIKROTIK_PORT', 8728),
    'user'     => env('MIKROTIK_USER', 'admin'),
    'password' => env('MIKROTIK_PASSWORD', ''),
    'ssl'      => filter_var(env('MIKROTIK_SSL', false), FILTER_VALIDATE_BOOLEAN),
    'timeout'  => (float) env('MIKROTIK_TIMEOUT', 3.0),

    // Interrupteur global : false = mode autonome sans routeur
    'enabled'  => filter_var(env('MIKROTIK_ENABLED', true), FILTER_VALIDATE_BOOLEAN),

    'hotspot_profile'     => env('MIKROTIK_HOTSPOT_PROFILE', 'default'),
    'hotspot_user_prefix' => env('MIKROTIK_USER_PREFIX', 'cm-'),

    // Production par défaut : élimination définitive de la mention 'test'
    'environment' => env('MIKROTIK_ENV', 'production'),

    /*
    |--------------------------------------------------------------------------
    | SSID Wi-Fi
    |--------------------------------------------------------------------------
    */
    'ssid_default' => env('MIKROTIK_SSID_DEFAULT'),

    'ssid_map' => collect(explode(',', (string) env('MIKROTIK_SSID_MAP', '')))
        ->filter(fn ($paire) => str_contains($paire, '='))
        ->mapWithKeys(function ($paire) {
            [$serveur, $ssid] = explode('=', $paire, 2);
            return [trim($serveur) => trim($ssid)];
        })
        ->all(),

    /*
    |--------------------------------------------------------------------------
    | Pool automatique de vouchers TEMPORAIRES
    |--------------------------------------------------------------------------
    */
    'voucher_pool_seuil'  => (int) env('VOUCHER_POOL_SEUIL', 10),
    'voucher_pool_taille' => (int) env('VOUCHER_POOL_TAILLE', 40),

];