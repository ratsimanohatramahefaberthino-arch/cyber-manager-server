<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::table('cyber_sessions', function (Blueprint $table) {
            // Pourquoi la session est en pause : manuel | coupure_poste | coupure_serveur | horloge
            $table->string('motif_pause', 24)->nullable();

            // Incident de coupure à l'origine du gel. Pas de FK volontairement (SQLite n'en crée pas
            // sur une table existante) : le nettoyage est assuré par le modèle Incident.
            $table->unsignedBigInteger('incident_id')->nullable();

            // Instant où le poste a redonné signe de vie après un gel (départ du délai d'abandon)
            $table->dateTime('poste_revenu_at')->nullable();

            // Cumul (secondes) du temps non facturé dû aux coupures, en plus de duree_hors_ligne_secondes
            $table->unsignedInteger('duree_coupure_secondes')->default(0);

            $table->index('incident_id', 'cyber_sessions_incident_idx');

            // Requêtes fréquentes : Poste::sessionActive() et la synchro Wi-Fi toutes les 5 s
            $table->index(['poste_id', 'etat'], 'cyber_sessions_poste_etat_idx');
            $table->index(['type_session', 'etat_wifi'], 'cyber_sessions_type_etatwifi_idx');
        });
    }

    public function down(): void
    {
        Schema::table('cyber_sessions', function (Blueprint $table) {
            // SQLite : un index doit être supprimé AVANT la colonne qu'il couvre
            $table->dropIndex('cyber_sessions_incident_idx');
            $table->dropIndex('cyber_sessions_poste_etat_idx');
            $table->dropIndex('cyber_sessions_type_etatwifi_idx');

            $table->dropColumn([
                'motif_pause',
                'incident_id',
                'poste_revenu_at',
                'duree_coupure_secondes',
            ]);
        });
    }
};