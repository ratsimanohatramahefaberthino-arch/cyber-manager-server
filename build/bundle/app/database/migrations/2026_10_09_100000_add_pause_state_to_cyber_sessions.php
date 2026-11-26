<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    private function estMysql(): bool
    {
        return in_array(DB::getDriverName(), ['mysql', 'mariadb'], true);
    }

    public function up(): void
    {
        // Étendre l'ENUM etat_wifi pour inclure "en_pause" (MySQL/MariaDB uniquement).
        // Sur SQLite, etat_wifi est déjà une chaîne libre (voir 2026_10_08_100000).
        if ($this->estMysql()) {
            DB::statement("
                ALTER TABLE cyber_sessions
                MODIFY COLUMN etat_wifi
                ENUM('connecte','deconnecte','termine','en_pause')
                NULL DEFAULT NULL
            ");
        }

        Schema::table('cyber_sessions', function (Blueprint $table) {
            $table->timestamp('pause_at')->nullable()->after('reconnecte_at');
            $table->unsignedInteger('duree_pause_secondes')->default(0)->after('pause_at');
        });
    }

    public function down(): void
    {
        Schema::table('cyber_sessions', function (Blueprint $table) {
            $table->dropColumn(['pause_at', 'duree_pause_secondes']);
        });

        if ($this->estMysql()) {
            DB::statement("
                ALTER TABLE cyber_sessions
                MODIFY COLUMN etat_wifi
                ENUM('connecte','deconnecte','termine')
                NULL DEFAULT NULL
            ");
        }
    }
};