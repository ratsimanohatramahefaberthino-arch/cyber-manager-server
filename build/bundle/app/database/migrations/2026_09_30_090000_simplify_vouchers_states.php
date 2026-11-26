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
        // États conservés : disponible, en_cours, utilise.
        // Les anciens expire/desactive/annule deviennent "utilise" :
        // rien n'est supprimé automatiquement par cette migration,
        // la suppression reste un geste manuel et volontaire.
        DB::table('vouchers')
            ->whereIn('etat', ['expire', 'desactive', 'annule'])
            ->update(['etat' => 'utilise']);

        // MySQL/MariaDB uniquement (voir 2026_09_28_210100) : sur SQLite la colonne reste une chaîne.
        if ($this->estMysql()) {
            DB::statement(
                "ALTER TABLE `vouchers` MODIFY `etat` ENUM('disponible','en_cours','utilise') NOT NULL DEFAULT 'disponible'"
            );
        }

        Schema::table('vouchers', function (Blueprint $table) {
            // Protection manuelle contre la suppression accidentelle
            // (ex : identifiant d'un poste Ethernet, accès familial...).
            // Indépendant du type réutilisable/usage unique et de la source.
            $table->boolean('protege')->default(false)->after('type_voucher');
        });
    }

    public function down(): void
    {
        Schema::table('vouchers', function (Blueprint $table) {
            $table->dropColumn('protege');
        });

        if ($this->estMysql()) {
            DB::statement(
                "ALTER TABLE `vouchers` MODIFY `etat` ENUM('disponible','en_cours','utilise','expire','desactive','annule') NOT NULL DEFAULT 'disponible'"
            );
        }
    }
};