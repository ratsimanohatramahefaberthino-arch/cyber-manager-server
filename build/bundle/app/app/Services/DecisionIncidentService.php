<?php

namespace App\Services;

use App\Models\Incident;
use App\Models\Poste;
use App\Models\Session;
use App\Support\SessionTransparence;
use Carbon\Carbon;
use Illuminate\Support\Facades\Log;

/**
 * Décisions du gérant sur les sessions gelées d'un incident.
 * Idempotent : une session déjà traitée est ignorée, jamais traitée deux fois.
 * Volontairement SANS grande transaction (appels routeur potentiellement lents, SQLite en IMMEDIATE).
 */
class DecisionIncidentService
{
    public const REPRENDRE = 'reprendre';
    public const CLOTURER  = 'cloturer';

    public function __construct(private PosteSessionService $sessions)
    {
    }

    /** @return array{ok:int, ignorees:int, erreurs:string[], incident_clos:bool} */
    public function appliquer(Incident $incident, string $action, ?int $sessionId = null, ?int $userId = null): array
    {
        $requete = Session::query()
            ->where('incident_id', $incident->id)
            ->where('etat', SessionTransparence::ETAT_SUSPENDUE);
        if ($sessionId !== null) {
            $requete->whereKey($sessionId);
        }

        $bilan = ['ok' => 0, 'ignorees' => 0, 'erreurs' => [], 'incident_clos' => false];

        foreach ($requete->pluck('id') as $id) {
            try {
                // Relecture juste avant d'agir (un autre onglet ou la maintenance a pu passer avant).
                $s = Session::with('poste')->find($id);
                if ($s === null || !SessionTransparence::estSuspendue($s) || $s->incident_id !== $incident->id) {
                    $bilan['ignorees']++;
                    continue;
                }

                $poste = $s->poste;
                if ($poste === null) {
                    $bilan['erreurs'][] = "Session #{$s->id} : poste introuvable.";
                    continue;
                }

                if ($action === self::REPRENDRE) {
                    if (!$this->posteEnLigne($poste)) {
                        $bilan['erreurs'][] = "{$poste->nom_poste} : poste pas encore revenu, rien n'a été repris.";
                        continue;
                    }
                    $this->sessions->reprendre($poste);
                } else {
                    $arretA = $s->date_heure_suspension ? Carbon::parse($s->date_heure_suspension) : null;
                    $this->sessions->cloturer($poste, 'coupure', $arretA);
                }
                $bilan['ok']++;
            } catch (\Throwable $e) {
                Log::error("Décision incident #{$incident->id} / session #{$id} : " . $e->getMessage());
                $bilan['erreurs'][] = "Session #{$id} : erreur interne, voir les journaux.";
            }
        }

        $bilan['incident_clos'] = $this->clorreSiTermine($incident->fresh(), $userId);

        return $bilan;
    }

    /** Acquittement manuel : refusé tant qu'il reste des sessions gelées ou une horloge non confirmée. */
    public function acquitter(Incident $incident, ?int $userId): ?string
    {
        if ($incident->sessions()->where('etat', SessionTransparence::ETAT_SUSPENDUE)->exists()) {
            return 'Des sessions sont encore gelées : reprenez-les ou clôturez-les d\'abord.';
        }
        if ($incident->type === Incident::TYPE_HORLOGE && ResilienceService::horlogeBloquee()) {
            return 'L\'heure du serveur n\'est pas encore confirmée.';
        }
        if ($incident->estOuvert()) {
            $incident->acquitter($userId);
        }

        return null;
    }

    private function clorreSiTermine(Incident $incident, ?int $userId): bool
    {
        return $incident->estOuvert() && $this->acquitter($incident, $userId) === null;
    }

    private function posteEnLigne(Poste $poste): bool
    {
        $hb = $poste->dernier_heartbeat;

        return $hb !== null && $hb->gt(now()->subSeconds(ParametresResilience::delaiInjoignableSecondes()));
    }
}