<?php

use App\Models\CyberSetting;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Route;

/*
|--------------------------------------------------------------------------
| Routes API
|--------------------------------------------------------------------------
*/

require __DIR__.'/desinstallation.php';

// Route de santé pour surveiller l'état du serveur et de la base (service Windows)
Route::get('/sante', function () {
    try {
        DB::select('select 1');
        $pouls   = (int) (CyberSetting::reseau('serveur.alive_at') ?? 0);
        $horloge = CyberSetting::reseau('serveur.horloge_bloquee') === '1';

        return response()->json([
            'ok'              => true,
            'base'            => true,
            'horloge_bloquee' => $horloge,
            'planificateur_s' => $pouls > 0 ? max(0, now()->getTimestamp() - $pouls) : null,
        ]);
    } catch (\Throwable) {
        return response()->json(['ok' => false, 'base' => false], 503);
    }
});