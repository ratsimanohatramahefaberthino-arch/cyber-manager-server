<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::table('tarifs', function (Blueprint $table) {
            // nullable : SQLite refuse d'ajouter une colonne NOT NULL sans valeur par défaut.
            // (Cette colonne est de toute façon supprimée par 2026_10_07_100000.)
            $table->string('nom')->nullable()->after('id');
            $table->unsignedInteger('montant_par_minute')->default(20);
            $table->unsignedInteger('montant_minimum')->default(300);
            $table->boolean('actif')->default(true);
            $table->boolean('personnalise')->default(false);
            $table->text('description')->nullable();
        });
    }

    public function down(): void
    {
        Schema::table('tarifs', function (Blueprint $table) {
            $table->dropColumn([
                'nom',
                'montant_par_minute',
                'montant_minimum',
                'actif',
                'personnalise',
                'description',
            ]);
        });
    }
};