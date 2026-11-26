<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Builder;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;

class Incident extends Model
{
    protected $table = 'cyber_incidents';

    public const TYPE_COUPURE_SERVEUR = 'coupure_serveur';
    public const TYPE_COUPURE_POSTE   = 'coupure_poste';
    public const TYPE_HORLOGE         = 'horloge_suspecte';

    public const STATUT_OUVERT = 'ouvert';
    public const STATUT_TRAITE = 'traite';

    protected $fillable = [
        'cle', 'type', 'debut_at', 'fin_at', 'duree_secondes', 'poste_id',
        'nb_sessions_figees', 'montant_fige_total', 'politique_appliquee',
        'statut', 'traite_par', 'traite_at', 'details',
    ];

    protected $casts = [
        'debut_at'           => 'datetime',
        'fin_at'             => 'datetime',
        'traite_at'          => 'datetime',
        'duree_secondes'     => 'integer',
        'nb_sessions_figees' => 'integer',
        'montant_fige_total' => 'integer',
        'details'            => 'array',
    ];

    protected static function booted(): void
    {
        // Pas de clé étrangère sur cyber_sessions.incident_id (voir migration) : on délie à la main.
        static::deleting(function (Incident $incident) {
            Session::where('incident_id', $incident->id)->update(['incident_id' => null]);
        });
    }

    public function sessions(): HasMany
    {
        return $this->hasMany(Session::class, 'incident_id');
    }

    public function poste(): BelongsTo
    {
        return $this->belongsTo(Poste::class);
    }

    public function traitePar(): BelongsTo
    {
        return $this->belongsTo(User::class, 'traite_par');
    }

    public function scopeOuverts(Builder $query): Builder
    {
        return $query->where('statut', self::STATUT_OUVERT);
    }

    public function estOuvert(): bool
    {
        return $this->statut === self::STATUT_OUVERT;
    }

    public function libelleType(): string
    {
        return match ($this->type) {
            self::TYPE_COUPURE_SERVEUR => 'Coupure du serveur',
            self::TYPE_COUPURE_POSTE   => 'Coupure d\'un poste',
            self::TYPE_HORLOGE         => 'Horloge à vérifier',
            default                    => 'Incident',
        };
    }

    /** Marque l'incident comme traité par le gérant. */
    public function acquitter(?int $userId = null): void
    {
        $this->update([
            'statut'     => self::STATUT_TRAITE,
            'traite_par' => $userId,
            'traite_at'  => now(),
        ]);
    }
}