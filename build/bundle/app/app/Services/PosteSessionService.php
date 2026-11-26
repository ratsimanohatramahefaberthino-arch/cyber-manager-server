<?php

namespace App\Services;

use App\Models\Incident;
use App\Models\Poste;
use App\Models\Session;
use Carbon\CarbonInterface;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Log;

/**
 * Logique partagée entre l'interface admin, l'API agent et la maintenance planifiée :
 * création / suspension / reprise / clôture de session et accès Internet du poste.
 *
 * POLITIQUE « coupure » (voir ResilienceService et ParametresResilience) :
 *  - le temps où le poste (ou le serveur) était injoignable n'est JAMAIS facturé :
 *    la session est figée rétroactivement au dernier signe de vie (suspendre() avec un instant passé) ;
 *  - au retour du poste : reprise par le client (défaut), automatique, ou décision du gérant ;
 *  - si personne ne reprend dans les délais réglés : clôture facturée jusqu'à l'instant du gel.
 *
 * MODE DÉGRADÉ : aucune méthode réseau ne lève d'exception ; la session s'ouvre / se ferme quand même.
 */
class PosteSessionService
{
    public function __construct(
        private MikroTikService $mikroTik,
        private TarifService $tarifs
    ) {}

    // ------------------------------------------------------------------
    // Cycle de vie de la session (base de données)
    // ------------------------------------------------------------------

    public function creerSession(Poste $poste, string $mode, ?int $duree): Session
    {
        try {
            $tarifId = $this->tarifs->tarifStandard()->id;
        } catch (\Throwable) {
            $tarifId = null;
        }

        $session = Session::create([
            'poste_id'              => $poste->id,
            'type_session'          => 'ethernet',
            'date_heure_debut'      => now(),
            'date_heure_fin_prevue' => $duree ? now()->addMinutes($duree) : null,
            'duree_prevue'          => $duree ?? 0,
            'etat'                  => 'en_cours',
            'tarif_id'              => $tarifId,
            'montant_initial'       => 0,
            'montant_total'         => 0,
            'description'           => "Session {$mode}" . ($duree ? " de {$duree} minutes." : '.'),
        ]);

        $session->mode_facturation = $mode;
        $session->save();

        return $session;
    }

    /**
     * Montant (Ar) de la session. Le mode « libre » n'est jamais facturé.
     *
     * @param int|null $secondes Durée facturable à utiliser ; null = durée consommée à l'instant T
     */
    public function montant(Session $session, ?int $secondes = null): int
    {
        if (($session->mode_facturation ?? 'forfait') === 'libre') {
            return 0;
        }

        try {
            $tarif   = $session->tarif ?: $this->tarifs->tarifStandard();
            $minutes = intdiv($secondes ?? $session->secondesConsommees(), 60);

            return $this->tarifs->montantTempsReel($minutes, $tarif);
        } catch (\Throwable) {
            return 0;
        }
    }

    /**
     * Suspend la session active : chrono figé, poste « suspendu ».
     * Base de données uniquement (à appeler dans une transaction) ; la coupure réseau
     * se fait ensuite via deconnecterReseau().
     *
     * @param CarbonInterface|null $a   Instant de gel (passé autorisé : gel rétroactif). Défaut : maintenant.
     *                                  Borné : jamais avant le début/la dernière reprise, jamais dans le futur.
     * @param string               $motif Session::PAUSE_* (manuel, coupure_poste, coupure_serveur, horloge)
     *
     * @return bool false si le poste n'a aucune session active
     */
    public function suspendre(
        Poste $poste,
        ?CarbonInterface $a = null,
        string $motif = Session::PAUSE_MANUEL,
        ?int $incidentId = null
    ): bool {
        $session = $poste->sessionActive();
        if ($session === null) {
            return false;
        }

        if ($session->etat === 'en_cours') {
            $session->update([
                'etat'                  => 'en_pause',
                'date_heure_suspension' => $this->bornerGel($session, $a),
                'motif_pause'           => $motif,
                'incident_id'           => $incidentId ?? $session->incident_id,
                'poste_revenu_at'       => null,
            ]);
        }
        $poste->update(['etat' => Poste::ETAT_SUSPENDU]);

        return true;
    }

    /**
     * Reprend la session active (si elle était en pause) et passe le poste « occupé ».
     * Le temps de pause n'est pas facturé ; pour un forfait, la fin prévue est repoussée d'autant.
     * Base de données uniquement ; la reconnexion réseau se fait ensuite.
     *
     * @return bool false si le poste n'a aucune session active
     */
    public function reprendre(Poste $poste): bool
    {
        $session = $poste->sessionActive();
        if ($session === null) {
            return false;
        }

        if ($session->etat === 'en_pause') {
            $maintenant = now();
            $pause = $session->date_heure_suspension
                ? max(0, $maintenant->getTimestamp() - $session->date_heure_suspension->getTimestamp())
                : 0;
            $coupure    = $session->estInterrompueParCoupure();
            $incidentId = $session->incident_id;

            $maj = [
                'etat'                  => 'en_cours',
                'date_heure_reprise'    => $maintenant,
                'duree_suspension'      => (int) $session->duree_suspension + $pause,
                'date_heure_suspension' => null,
                'motif_pause'           => null,
                'poste_revenu_at'       => null,
            ];
            if ($coupure) {
                // Sous-ensemble de duree_suspension, uniquement pour l'affichage « temps non facturé »
                $maj['duree_coupure_secondes'] = (int) $session->duree_coupure_secondes + $pause;
            }
            if ($session->date_heure_fin_prevue !== null && $pause > 0) {
                $maj['date_heure_fin_prevue'] = $session->date_heure_fin_prevue->copy()->addSeconds($pause);
            }

            $session->update($maj);
            $this->actualiserIncidentPoste($incidentId);
        }

        $poste->update(['etat' => Poste::ETAT_OCCUPE]);

        return true;
    }

    /**
     * Clôture la session active : calcule et fige le montant, poste « désactivé ».
     * Base de données uniquement (à appeler dans une transaction) ; la coupure réseau
     * se fait ensuite via deconnecterReseau().
     *
     * @param CarbonInterface|null $arretA Instant (passé) où la consommation s'est réellement arrêtée
     *                                     (expiration d'un forfait). Ignoré si la session est en pause
     *                                     (son chrono est déjà figé).
     */
    public function cloturer(Poste $poste, string $motif = 'client', ?CarbonInterface $arretA = null): ?Session
    {
        $session = $poste->sessionActive();
        if ($session === null) {
            return null;
        }

        $brut     = $session->secondesConsommees();   // avant le changement d'état
        $secondes = $brut;
        $fin      = now();

        // Session en pause : la pause en cours doit être ajoutée à duree_suspension, sinon
        // secondesConsommees() d'une session terminée la compterait comme consommée.
        $pauseEnCours = 0;
        $coupure      = false;
        if ($session->etat === 'en_pause' && $session->date_heure_suspension !== null) {
            $pauseEnCours = max(0, $fin->getTimestamp() - $session->date_heure_suspension->getTimestamp());
            $coupure      = $session->estInterrompueParCoupure();
        }

        if ($arretA !== null && $session->etat === 'en_cours' && $session->date_heure_debut !== null) {
            $arret = $arretA->copy();
            if ($arret->lt($session->date_heure_debut)) {
                $arret = $session->date_heure_debut->copy();
            }
            if ($arret->lt($fin)) {
                // Le chrono comptait en continu : on retire le temps écoulé depuis l'arrêt réel
                $secondes = max(0, $brut - ($fin->getTimestamp() - $arret->getTimestamp()));
                $fin      = $arret;
            }
        }

        $montant = $this->montant($session, $secondes);

        $session->update([
            'etat'                   => 'terminee',
            'date_heure_fin_reelle'  => $fin,
            'motif_fin'              => $motif,
            'duree_consommee'        => intdiv($secondes, 60),
            'montant_consomme'       => $montant,
            'montant_total'          => $montant,
            'duree_suspension'       => (int) $session->duree_suspension + $pauseEnCours,
            'duree_coupure_secondes' => (int) $session->duree_coupure_secondes + ($coupure ? $pauseEnCours : 0),
        ]);

        $this->actualiserIncidentPoste($session->incident_id);

        // Fin de session (fermeture, expiration, abandon) : le poste passe en « Désactivé ».
        // Il ne redevient disponible que lorsque le gérant clique sur « Activer ».
        $poste->update(['etat' => Poste::ETAT_DESACTIVE]);

        return $session;
    }

    // ------------------------------------------------------------------
    // Expiration des forfaits
    // ------------------------------------------------------------------

    /** Vrai si la session est un forfait en cours dont le temps est épuisé. */
    public function sessionExpiree(Session $session): bool
    {
        return $session->etat === 'en_cours'
            && ($session->mode_facturation ?? 'forfait') === 'forfait'
            && (int) $session->duree_prevue > 0
            && $session->secondesConsommees() >= (int) $session->duree_prevue * 60;
    }

    /**
     * Clôture la session du poste si son forfait est épuisé, puis coupe Internet.
     * Idempotent et sûr en concurrence : appelable depuis le heartbeat ET depuis la maintenance.
     */
    public function expirerSiNecessaire(Poste $poste): ?Session
    {
        $apercu = $poste->sessionActive();
        if ($apercu === null || !$this->sessionExpiree($apercu)) {
            return null;
        }

        if (ResilienceService::horlogeBloquee()) {
            return null;   // horloge suspecte : aucun calcul automatique
        }

        $session = DB::transaction(function () use ($poste) {
            $p = Poste::whereKey($poste->id)->lockForUpdate()->first();
            $s = $p?->sessionActive();
            if ($s === null || !$this->sessionExpiree($s)) {
                return null;
            }

            // Instant exact où le forfait a été épuisé (en tenant compte des temps non facturés)
            $deductions = (int) $s->duree_suspension + (int) $s->duree_hors_ligne_secondes + (int) $s->duree_pause_secondes;
            $arret      = $s->date_heure_debut->copy()->addSeconds((int) $s->duree_prevue * 60 + $deductions);

            return $this->cloturer($p, 'expiration', $arret->isFuture() ? null : $arret);
        });

        if ($session === null) {
            return null;
        }

        Log::info("Forfait expiré : session #{$session->id} du poste {$poste->nom_poste} clôturée ({$session->montant_total} Ar).");
        $this->deconnecterReseau($poste);

        return $session;
    }

    // ------------------------------------------------------------------
    // Poste injoignable / retour en ligne
    // ------------------------------------------------------------------

    /**
     * Micro-coupure : le poste redonne signe de vie après une panne plus longue que le délai
     * d'injoignabilité mais sans avoir été figé. Le temps d'injoignabilité n'est pas facturé.
     *
     * @return int Secondes déduites
     */
    public function deduireTempsHorsLigne(Poste $poste, ?CarbonInterface $dernierSigne): int
    {
        if ($dernierSigne === null) {
            return 0;
        }

        return DB::transaction(function () use ($poste, $dernierSigne) {
            $p = Poste::whereKey($poste->id)->lockForUpdate()->first();
            $s = $p?->sessionActive();
            if ($s === null || $s->etat !== 'en_cours') {
                return 0;   // une session figée n'est jamais déduite deux fois
            }

            // La panne ne peut pas avoir commencé avant le début ou la dernière reprise de la session
            $debutPanne = max(
                $dernierSigne->getTimestamp(),
                $s->date_heure_debut?->getTimestamp() ?? 0,
                $s->date_heure_reprise?->getTimestamp() ?? 0
            );
            $panne = now()->getTimestamp() - $debutPanne;
            if ($panne <= Poste::delaiHorsLigne()) {
                return 0;
            }

            $s->update(['duree_hors_ligne_secondes' => (int) $s->duree_hors_ligne_secondes + $panne]);
            Log::info("Poste {$poste->nom_poste} de retour après {$panne} s : temps de panne déduit de la session #{$s->id}.");

            return $panne;
        });
    }

    /**
     * Le poste donne signe de vie alors que sa session est figée par une coupure :
     * enregistre son retour (départ du délai d'abandon) et applique la politique de reprise.
     * Idempotent : n'agit qu'au premier signe de vie après le gel.
     *
     * @return bool vrai si la session active du poste est (ou était) figée par une coupure
     */
    public function traiterRetourSiGele(Poste $poste): bool
    {
        $session = $poste->sessionActive();
        if ($session === null || !$session->estInterrompueParCoupure()) {
            return false;
        }
        if ($session->poste_revenu_at !== null) {
            return true;   // déjà traité
        }

        $session->update(['poste_revenu_at' => now()]);

        // Fin de l'incident d'un poste seul : la durée de la coupure est connue
        if ($session->incident_id) {
            $incident = Incident::find($session->incident_id);
            if ($incident && $incident->type === Incident::TYPE_COUPURE_POSTE && $incident->fin_at === null) {
                $incident->update([
                    'fin_at'         => now(),
                    'duree_secondes' => max(0, now()->getTimestamp() - $incident->debut_at->getTimestamp()),
                ]);
            }
        }

        if (ParametresResilience::politiqueReprise() === ParametresResilience::POLITIQUE_AUTO
            && !ResilienceService::horlogeBloquee()) {
            $repris = DB::transaction(function () use ($poste) {
                $p = Poste::whereKey($poste->id)->lockForUpdate()->first();
                return $p !== null && $this->reprendre($p);
            });

            if ($repris) {
                Log::info("Poste {$poste->nom_poste} : session reprise automatiquement après la coupure (politique « auto »).");
                $this->connecterReseau($poste);
            }
        }

        return true;
    }

    /**
     * Retour en ligne d'un poste (redémarrage, coupure) : gère une session figée, sinon déduit
     * la panne, applique une éventuelle expiration, puis rétablit Internet si la session tourne.
     */
    public function traiterRetourEnLigne(Poste $poste, ?CarbonInterface $dernierSigne): void
    {
        if ($this->traiterRetourSiGele($poste)) {
            return;
        }

        $this->deduireTempsHorsLigne($poste, $dernierSigne);

        if ($this->expirerSiNecessaire($poste) !== null) {
            return;
        }

        $session = $poste->sessionActive();
        if ($session !== null && $session->etat === 'en_cours') {
            $this->connecterReseau($poste);
        }
    }

    /**
     * Clôture une session figée par une coupure si personne ne l'a reprise dans les délais :
     *  - poste revenu : abandon_apres_retour_minutes après son retour ;
     *  - poste jamais revenu : abandon_poste_absent_minutes après le gel.
     * Facturée jusqu'à l'instant du gel ; Internet reste coupé. Politique « gérant » : jamais d'abandon automatique.
     */
    public function abandonnerSiNecessaire(Poste $poste): ?Session
    {
        if (ParametresResilience::politiqueReprise() === ParametresResilience::POLITIQUE_GERANT
            || ResilienceService::horlogeBloquee()) {
            return null;
        }

        $session = DB::transaction(function () use ($poste) {
            $p = Poste::whereKey($poste->id)->lockForUpdate()->first();
            $s = $p?->sessionActive();
            if ($s === null || !$s->estInterrompueParCoupure()) {
                return null;
            }

            if ($s->poste_revenu_at !== null) {
                $delai = ParametresResilience::abandonApresRetourMinutes();
                if ($delai <= 0 || $s->poste_revenu_at->gt(now()->subMinutes($delai))) {
                    return null;
                }
            } else {
                $delai = ParametresResilience::abandonPosteAbsentMinutes();
                $gel   = $s->date_heure_suspension;
                if ($delai <= 0 || $gel === null || $gel->gt(now()->subMinutes($delai))) {
                    return null;
                }
            }

            return $this->cloturer($p, 'coupure');
        });

        if ($session === null) {
            return null;
        }

        Log::warning("Poste {$poste->nom_poste} : session #{$session->id} clôturée après la coupure sans reprise ({$session->montant_total} Ar, facturée jusqu'au gel).");
        $this->deconnecterReseau($poste);

        return $session;
    }

    // ------------------------------------------------------------------
    // Réseau : AccesInternetService est l'unique autorité (liaison par adresse MAC)
    // ------------------------------------------------------------------

    /** Le compte HotSpot existe-t-il sur MikroTik ? (conservé pour l'existant) */
    public function compteHotspotExiste(string $username): ?bool
    {
        if (!$this->mikrotikActif()) {
            return null;
        }

        try {
            return $this->mikroTik->findHotspotUser($username) !== null;
        } catch (\Throwable $e) {
            Log::warning('MikroTik : vérification du compte HotSpot impossible (' . $e->getMessage() . ').');
            return null;
        }
    }

    public function mikrotikActif(): bool
    {
        return (bool) config('mikrotik.enabled', true);
    }

    /** @return bool|null true = autorisé, false = échec routeur (mode dégradé), null = aucun routeur */
    public function connecterReseau(Poste $poste): ?bool
    {
        return app(AccesInternetService::class)->autoriser($poste);
    }

    /** @return bool|null true = coupé, false = échec routeur (mode dégradé), null = aucun routeur */
    public function deconnecterReseau(Poste $poste): ?bool
    {
        return app(AccesInternetService::class)->couper($poste);
    }

    /**
     * Aligne le routeur sur l'état réel des sessions (auto-réparation).
     *
     * @return array{connectes:int, coupes:int, erreurs:int}
     */
    public function reconcilierReseau(): array
    {
        return app(AccesInternetService::class)->reconcilier();
    }

    // ------------------------------------------------------------------
    // Outils internes
    // ------------------------------------------------------------------

    /** Instant de gel borné : pas avant le début / la dernière reprise, pas dans le futur. */
    private function bornerGel(Session $session, ?CarbonInterface $a): CarbonInterface
    {
        $maintenant = now();
        $gel        = $a !== null ? $a->copy() : $maintenant->copy();

        if ($gel->gt($maintenant)) {
            $gel = $maintenant->copy();
        }
        if ($session->date_heure_debut !== null && $gel->lt($session->date_heure_debut)) {
            $gel = $session->date_heure_debut->copy();
        }
        if ($session->date_heure_reprise !== null && $gel->lt($session->date_heure_reprise)) {
            $gel = $session->date_heure_reprise->copy();
        }

        return $gel;
    }

    /** L'incident d'un poste seul se résout tout seul quand plus aucune session n'est figée. */
    private function actualiserIncidentPoste(?int $incidentId): void
    {
        if ($incidentId === null) {
            return;
        }

        $incident = Incident::find($incidentId);
        if ($incident === null || $incident->type !== Incident::TYPE_COUPURE_POSTE || !$incident->estOuvert()) {
            return;
        }

        if (!Session::where('incident_id', $incident->id)->where('etat', 'en_pause')->exists()) {
            $incident->acquitter(null);
        }
    }
}