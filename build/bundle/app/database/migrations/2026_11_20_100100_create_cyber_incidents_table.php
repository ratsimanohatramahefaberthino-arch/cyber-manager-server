<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('cyber_incidents', function (Blueprint $table) {
            $table->id();

            // Clé d'idempotence : « serveur:<epoch du dernier signe de vie> » ou « poste:<id>:<epoch> ».
            // Garantit qu'un même redémarrage ne crée jamais deux incidents.
            $table->string('cle', 80)->unique();

            // coupure_serveur | coupure_poste | horloge_suspecte
            $table->string('type', 24);

            $table->dateTime('debut_at');                       // dernier signe de vie
            $table->dateTime('fin_at')->nullable();             // retour (redémarrage du serveur / du poste)
            $table->unsignedInteger('duree_secondes')->nullable();

            $table->foreignId('poste_id')->nullable()->constrained('postes')->nullOnDelete();

            $table->unsignedInteger('nb_sessions_figees')->default(0);
            $table->unsignedInteger('montant_fige_total')->default(0);   // Ar

            // Politique de reprise en vigueur au moment de l'incident (client | gerant | auto)
            $table->string('politique_appliquee', 16)->nullable();

            // ouvert | traite
            $table->string('statut', 16)->default('ouvert');
            $table->foreignId('traite_par')->nullable()->constrained('users')->nullOnDelete();
            $table->dateTime('traite_at')->nullable();

            $table->json('details')->nullable();

            $table->timestamps();

            $table->index(['type', 'debut_at']);
            $table->index('statut');
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('cyber_incidents');
    }
};