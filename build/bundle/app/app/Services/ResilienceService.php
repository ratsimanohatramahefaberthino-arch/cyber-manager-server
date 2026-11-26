<?php

namespace App\Services;

use App\Models\CyberSetting;
use App\Models\Incident;
use App\Models\Poste;
use App\Models\Session;
use Illuminate\Support\Carbon;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Log;

/**
 * Résilience aux coupures de courant : gel rétroactif des sessions au dernier signe de vie.
 *
 *  - Événement A : un poste seul tombe (serveur actif)      → figerPosteInjoignable()
 *  - Événement B : le serveur redémarre après une coupure   → traiterDemarrageServeur()
 *
 * Invariant : le client n'est jamais facturé pour le temps où le poste (ou le serveur)
 * était éteint.
 */
class ResilienceService
{
    public const CLE_ALIVE           = 'serveur.alive_at';          // epoch du dernier pouls du serveur
    public const CLE_DEMARRE         = 'serveur.demarre_at';        // epoch du dernier démarrage traité
    public const CLE_ARRET_PROPRE    = 'serveur.arret_propre';      // '1' si le service a signalé un arrêt contrôlé
    public const CLE_HORLOGE_BLOQUEE = 'serveur.horloge_bloquee';   // '1' tant que le gérant n'a pas confirmé l'heure

    /** Écart (s) au-delà duquel le serveur est considéré comme ayant été arrêté. */
    public const SEUIL_ARRET_SECONDES = 30;

    /** Écart (jours) jugé aberrant : l'horloge est à vérifier avant tout calcul. */
    public const ECART_ABERRANT_JOURS = 7;

    public function __construct(private PosteSessionService $sessions)
    {
    }

    // ------------------------------------------------------------------
    // Garde d'horloge
    // ------------------------------------------------------------------

    /** Vrai tant que l'horloge est suspecte : aucune clôture ni gel automatique. */
    public static function horlogeBloquee(): bool
    {
        try {
            return CyberSetting::reseau(self::CLE_HORLOGE_BLOQUEE) === '1';
        } catch (\Throwable) {
            return false;
        }
    }

    /** Action du gérant : « L'heure est correcte, continuer ». */
    public function confirmerHorloge(): void
    {
        CyberSetting::definir(self::CLE_HORLOGE_BLOQUEE, '0');
        CyberSetting::definir(self::CLE_ALIVE, (string) now()->getTimestamp());
        Log::info('Horloge confirmée par le gérant : automatismes rétablis.');
    }

    // ------------------------------------------------------------------
    // Pouls du serveur (toutes les 10 s, via serveur:pulse)
    // ------------------------------------------------------------------

    public function pulse(): void
    {
        $maintenant = now()->getTimestamp();
        $vu         = (int) (CyberSetting::reseau(self::CLE_ALIVE) ?? 0);

        // Horloge revenue en arrière : on ne piétine pas la preuve du dernier signe de vie.
        if ($vu > 0 && $maintenant < $vu - 5 && ParametresResilience::horlogeGardeActive()) {
            if (!self::horlogeBloquee()) {
                CyberSetting::definir(self::CLE_HORLOGE_BLOQUEE, '1');
                Log::warning('Horloge du serveur revenue en arrière : automatismes suspendus jusqu\'à confirmation du gérant.');
            }
            return;
        }

        CyberSetting::definir(self::CLE_ALIVE, (string) $maintenant);
    }

    // ------------------------------------------------------------------
    // Fenêtre de reconnexion après redémarrage du serveur
    // ------------------------------------------------------------------

    /** Pendant cette fenêtre : aucune clôture ni abandon automatique (les PC mettent du temps à revenir). */
    public function fenetreReconnexionActive(): bool
    {
        $debut = (int) (CyberSetting::reseau(self::CLE_DEMARRE) ?? 0);
        if ($debut <= 0) {
            return false;
        }

        return now()->getTimestamp() < $debut + ParametresResilience::fenetreReconnexionMinutes() * 60;
    }

    // ------------------------------------------------------------------
    // Événement B : redémarrage du serveur (appelé par serveur:demarrage, AVANT le serveur web)
    // ------------------------------------------------------------------

    /**
     * @return array{type:string, incident_id:?int, sessions_figees:int, montant_fige:int, ecart_secondes:int}
     */
public function traiterDemarrageServeur(): array
    {
        $now        = now();
        $maintenant = $now->getTimestamp();

        $bilan = [
            'type'            => 'normal',
            'incident_id'     => null,
            'sessions_figees' => 0,
            'montant_fige'    => 0,
            'ecart_secondes'  => 0,
        ];

        try {
            $alive = (int) (CyberSetting::reseau(self::CLE_ALIVE) ?? 0);
            if ($alive <= 0) {
                $this->marquerDemarrage($maintenant);
                $bilan['type'] = 'premier_demarrage';
                return $bilan;
            }

            $dernierHb   = Poste::query()->max('dernier_heartbeat');
            $hb          = $dernierHb ? Carbon::parse($dernierHb)->getTimestamp() : 0;
            $derniereVie = max($alive, $hb);
            $ecart       = $maintenant - $derniereVie;
            $debut       = $this->depuisTimestamp($derniereVie);
            $arretPropre = CyberSetting::reseau(self::CLE_ARRET_PROPRE) === '1';

            $bilan['ecart_secondes'] = $ecart;

            // 1) Horloge suspecte
            if (ParametresResilience::horlogeGardeActive()
                && ($ecart < -5 || $ecart > self::ECART_ABERRANT_JOURS * 86400)) {
                $incident = $this->creerIncident(Incident::TYPE_HORLOGE, "horloge:{$derniereVie}", $debut, $now, [
                    'ecart_secondes' => $ecart,
                ]);

                CyberSetting::definir(self::CLE_HORLOGE_BLOQUEE, '1');
                CyberSetting::definir(self::CLE_DEMARRE, (string) $maintenant);
                CyberSetting::definir(self::CLE_ARRET_PROPRE, '0');

                Log::warning("Démarrage : horloge suspecte (écart {$ecart} s). Automatismes suspendus.");
                $bilan['type']        = 'horloge_suspecte';
                $bilan['incident_id'] = $incident->id;
                return $bilan;
            }

            // 2) Redémarrage rapide : rien à figer
            if ($ecart <= self::SEUIL_ARRET_SECONDES) {
                $this->marquerDemarrage($maintenant);
                return $bilan;
            }

            // 3) Coupure : figer les sessions en cours sans jamais lever d'exception bloquante
            $enCours = Session::query()
                ->where('etat', 'en_cours')
                ->whereNotNull('poste_id')
                ->with('poste')
                ->get();

            $bilan['type'] = 'coupure_serveur';

            if ($enCours->isEmpty() && $arretPropre) {
                $this->marquerDemarrage($maintenant);
                $bilan['type'] = 'arret_sans_session';
                return $bilan;
            }

            $incident = $this->creerIncident(Incident::TYPE_COUPURE_SERVEUR, "serveur:{$derniereVie}", $debut, $now, [
                'arret_propre'   => $arretPropre,
                'ecart_secondes' => $ecart,
            ]);

            foreach ($enCours as $session) {
                try {
                    $poste = $session->poste;
                    if ($poste === null) continue;

                    $gel = $poste->dernier_heartbeat ?? $session->date_heure_debut ?? $now;

                    DB::transaction(function () use ($poste, $gel, $incident) {
                        $p = Poste::whereKey($poste->id)->lockForUpdate()->first();
                        if ($p !== null) {
                            $this->sessions->suspendre($p, $gel, Session::PAUSE_COUPURE_SERVEUR, $incident->id);
                        }
                    });
                } catch (\Throwable $exSession) {
                    Log::warning("Impossible de figer la session #{$session->id} post-coupure : " . $exSession->getMessage());
                }
            }

            $figees  = Session::where('incident_id', $incident->id)->get();
            $montant = (int) $figees->sum(fn (Session $s) => $this->sessions->montant($s));

            $incident->update([
                'nb_sessions_figees'  => $figees->count(),
                'montant_fige_total'  => $montant,
                'politique_appliquee' => ParametresResilience::politiqueReprise(),
                'statut'              => $figees->isEmpty() ? Incident::STATUT_TRAITE : Incident::STATUT_OUVERT,
                'traite_at'           => $figees->isEmpty() ? $now : null,
            ]);

            $this->marquerDemarrage($maintenant);
            Log::warning("Coupure serveur traitée : {$figees->count()} session(s) figée(s) ({$montant} Ar).");

            $bilan['incident_id']     = $incident->id;
            $bilan['sessions_figees'] = $figees->count();
            $bilan['montant_fige']    = $montant;
        } catch (\Throwable $e) {
            Log::error("Erreur non bloquante dans traiterDemarrageServeur : " . $e->getMessage());
            $this->marquerDemarrage($maintenant);
            $bilan['type'] = 'erreur_recuperee';
        }

        return $bilan;
    }

    // ------------------------------------------------------------------
    // Événement A : un poste seul tombe (appelé par postes:maintenance)
    // ------------------------------------------------------------------

    /**
     * Fige rétroactivement la session « en cours » d'un poste injoignable depuis plus que le délai
     * d'injoignabilité, puis coupe son accès Internet.
     */
    public function figerPosteInjoignable(Poste $poste): ?Session
    {
        if (self::horlogeBloquee()) {
            return null;
        }

        $delai = ParametresResilience::delaiInjoignableSecondes();

        $session = DB::transaction(function () use ($poste, $delai) {
            $p = Poste::whereKey($poste->id)->lockForUpdate()->first();
            $s = $p?->sessionActive();
            if ($s === null || $s->etat !== 'en_cours') {
                return null;
            }

            $dernier = $p->dernier_heartbeat;
            if ($dernier !== null && $dernier->gt(now()->subSeconds($delai))) {
                return null;   // revenu entre-temps
            }

            $debut = $s->date_heure_debut ?? now();
            $gel   = ($dernier === null || $dernier->lt($debut)) ? $debut->copy() : $dernier->copy();

            $incident = Incident::firstOrCreate(
                ['cle' => "poste:{$p->id}:{$gel->getTimestamp()}"],
                [
                    'type'                => Incident::TYPE_COUPURE_POSTE,
                    'debut_at'            => $gel,
                    'poste_id'            => $p->id,
                    'nb_sessions_figees'  => 1,
                    'politique_appliquee' => ParametresResilience::politiqueReprise(),
                    'statut'              => Incident::STATUT_OUVERT,
                    'details'             => ['poste' => $p->nom_poste, 'session_id' => $s->id],
                ]
            );

            $this->sessions->suspendre($p, $gel, Session::PAUSE_COUPURE_POSTE, $incident->id);

            $figee = $s->fresh();
            $incident->update(['montant_fige_total' => $this->sessions->montant($figee)]);

            return $figee;
        });

        if ($session === null) {
            return null;
        }

        Log::warning("Poste {$poste->nom_poste} injoignable : session #{$session->id} figée au dernier signe de vie ({$session->date_heure_suspension}).");
        $this->sessions->deconnecterReseau($poste);

        return $session;
    }

    // ------------------------------------------------------------------
    // Outils internes
    // ------------------------------------------------------------------

    private function marquerDemarrage(int $maintenant): void
    {
        CyberSetting::definir(self::CLE_DEMARRE, (string) $maintenant);
        CyberSetting::definir(self::CLE_ARRET_PROPRE, '0');
        CyberSetting::definir(self::CLE_ALIVE, (string) $maintenant);
    }

    private function creerIncident(string $type, string $cle, Carbon $debut, Carbon $fin, array $details = []): Incident
    {
        return Incident::firstOrCreate(['cle' => $cle], [
            'type'                => $type,
            'debut_at'            => $debut,
            'fin_at'              => $fin,
            'duree_secondes'      => max(0, $fin->getTimestamp() - $debut->getTimestamp()),
            'politique_appliquee' => ParametresResilience::politiqueReprise(),
            'statut'              => Incident::STATUT_OUVERT,
            'details'             => $details,
        ]);
    }

    /** Carbon 3 renvoie de l'UTC par défaut : on impose le fuseau de l'application. */
    private function depuisTimestamp(int $ts): Carbon
    {
        return Carbon::createFromTimestamp($ts, config('app.timezone'));
    }
}