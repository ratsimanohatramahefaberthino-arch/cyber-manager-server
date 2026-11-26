<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Builder;
use Illuminate\Database\Eloquent\Relations\HasMany;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;

class Session extends Model
{
    protected $table = 'cyber_sessions';

    public const WIFI_CONNECTE   = 'connecte';
    public const WIFI_DECONNECTE = 'deconnecte';
    public const WIFI_TERMINE    = 'termine';
    public const WIFI_EN_PAUSE   = 'en_pause';

    /** Motifs de pause (colonne motif_pause) */
    public const PAUSE_MANUEL          = 'manuel';
    public const PAUSE_COUPURE_POSTE   = 'coupure_poste';
    public const PAUSE_COUPURE_SERVEUR = 'coupure_serveur';
    public const PAUSE_HORLOGE         = 'horloge';

    /** Pauses causées par une coupure (le client n'y est pour rien : temps non facturé) */
    public const PAUSES_COUPURE = [
        self::PAUSE_COUPURE_POSTE,
        self::PAUSE_COUPURE_SERVEUR,
        self::PAUSE_HORLOGE,
    ];

    protected $fillable = [
        'poste_id', 'type_session',
        'date_heure_debut', 'date_heure_fin_prevue', 'date_heure_fin_reelle',
        'date_heure_suspension', 'date_heure_reprise', 'duree_suspension',
        'duree_prevue', 'duree_consommee', 'temps_restant',
        'montant_initial', 'montant_recharge', 'montant_total',
        'montant_consomme', 'montant_restant', 'montant_a_reverser',
        'description', 'etat', 'motif_fin', 'tarif_id', 'voucher_id',
        'volume_entree', 'volume_sortie', 'volume_total',
        'mikrotik_active_id', 'hotspot_username', 'client_mac', 'client_ip',
        'ssid', 'appareil_wifi_id', 'derniere_sync_at',
        'etat_wifi', 'deconnecte_at', 'reconnecte_at', 'duree_hors_ligne_secondes',
        'pause_at', 'duree_pause_secondes',
        'alarme_active', 'alarme_type', 'alarme_duree_minutes', 'alarme_montant',
        'alarme_fin_prevue', 'alarme_derniere_fin_at', 'alarme_declenchee_at',
        'alarme_message', 'alarme_historique', 'alarme_secondes_restantes',
        'alarme_total_secondes',
        'remarque',
        // Résilience aux coupures
        'motif_pause', 'incident_id', 'poste_revenu_at', 'duree_coupure_secondes',
    ];

    protected $casts = [
        'date_heure_debut' => 'datetime',
        'date_heure_fin_prevue' => 'datetime',
        'date_heure_fin_reelle' => 'datetime',
        'date_heure_suspension' => 'datetime',
        'date_heure_reprise' => 'datetime',
        'derniere_sync_at' => 'datetime',
        'deconnecte_at' => 'datetime',
        'reconnecte_at' => 'datetime',
        'pause_at' => 'datetime',
        'alarme_fin_prevue' => 'datetime',
        'alarme_derniere_fin_at' => 'datetime',
        'alarme_declenchee_at' => 'datetime',
        'alarme_active' => 'boolean',
        'duree_hors_ligne_secondes' => 'integer',
        'duree_pause_secondes' => 'integer',
        'alarme_duree_minutes' => 'integer',
        'alarme_montant' => 'integer',
        'alarme_historique' => 'array',
        'alarme_secondes_restantes' => 'integer',
        'alarme_total_secondes' => 'integer',
        'poste_revenu_at' => 'datetime',
        'duree_coupure_secondes' => 'integer',
    ];

    public function poste(): BelongsTo { return $this->belongsTo(Poste::class); }
    public function recharges(): HasMany { return $this->hasMany(Recharge::class); }
    public function restitutions(): HasMany { return $this->hasMany(Restitution::class); }
    public function tarif(): BelongsTo { return $this->belongsTo(Tarif::class); }
    public function voucher(): BelongsTo { return $this->belongsTo(Voucher::class); }
    public function appareilWifi(): BelongsTo { return $this->belongsTo(AppareilWifi::class, 'appareil_wifi_id'); }
    public function incident(): BelongsTo { return $this->belongsTo(Incident::class, 'incident_id'); }

    public function estConnecte(): bool   { return $this->etat_wifi === self::WIFI_CONNECTE; }
    public function estDeconnecte(): bool { return $this->etat_wifi === self::WIFI_DECONNECTE; }
    public function estEnPause(): bool    { return $this->etat_wifi === self::WIFI_EN_PAUSE; }
    public function estTermine(): bool    { return $this->etat_wifi === self::WIFI_TERMINE; }

    public function compteurActif(): bool
    {
        return $this->etat_wifi === self::WIFI_CONNECTE;
    }

    /** Session de poste gelée à cause d'une coupure (et non mise en pause volontairement). */
    public function estInterrompueParCoupure(): bool
    {
        return $this->etat === 'en_pause'
            && in_array($this->motif_pause, self::PAUSES_COUPURE, true);
    }

    /** Sessions de poste gelées à cause d'une coupure. */
    public function scopeInterrompues(Builder $query): Builder
    {
        return $query->where('etat', 'en_pause')
            ->whereIn('motif_pause', self::PAUSES_COUPURE);
    }

    /**
     * Temps non facturé dû aux coupures (secondes) : panne détectée par déduction
     * + temps gelé rétroactivement. Sert à l'affichage « Temps non facturé : coupure X min ».
     */
    public function tempsNonFactureSecondes(): int
    {
        return (int) $this->duree_hors_ligne_secondes + (int) $this->duree_coupure_secondes;
    }

    /**
     * Secondes facturables écoulées (hors périodes déconnecté / en pause).
     *
     * On travaille sur des timestamps entiers : avec Carbon 3, diffInSeconds()
     * renvoie un float signé, ce qui provoquait des conversions implicites
     * float → int (dépréciées en PHP 8.1+).
     */
    public function secondesConsommees(): int
    {
        if (!$this->date_heure_debut) return 0;

        if ($this->estTermine()) {
            return max(0, (int) $this->duree_consommee * 60);
        }

        if ($this->estEnPause() && $this->pause_at) {
            $fin = $this->pause_at;
        } elseif ($this->estDeconnecte() && $this->deconnecte_at) {
            $fin = $this->deconnecte_at;
        } elseif ($this->etat === 'en_pause' && $this->date_heure_suspension) {
            $fin = $this->date_heure_suspension;      // postes Ethernet : chrono figé pendant la suspension
        } elseif ($this->etat === 'terminee' && $this->date_heure_fin_reelle) {
            $fin = $this->date_heure_fin_reelle;      // postes Ethernet : session clôturée
        } else {
            $fin = now();
        }

        $total = $fin->getTimestamp() - $this->date_heure_debut->getTimestamp();
        $total -= (int) $this->duree_hors_ligne_secondes;
        $total -= (int) $this->duree_pause_secondes;
        $total -= (int) $this->duree_suspension;      // pauses Ethernet déjà terminées

        return max(0, (int) $total);
    }
}