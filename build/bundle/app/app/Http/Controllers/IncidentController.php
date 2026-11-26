<?php

namespace App\Http\Controllers;

use App\Models\Incident;
use App\Models\Session;
use App\Services\BandeauIncidents;
use App\Services\DecisionIncidentService as Decision;
use App\Services\PosteSessionService;
use App\Services\ResilienceService;
use App\Support\SessionTransparence;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;

class IncidentController extends Controller
{
    public function __construct(
        private Decision $decision,
        private PosteSessionService $sessions,
        private ResilienceService $resilience,
    ) {
    }

    private function peutDecider(): bool
    {
        return auth()->user()?->role === 'admin';   // ADAPTER à votre modèle User
    }

    public function index(Request $request)
    {
        $statut = in_array($request->query('statut'), ['ouvert', 'traite'], true) ? $request->query('statut') : 'tous';

        $incidents = Incident::query()
            ->with('poste')
            ->when($statut !== 'tous', fn ($q) => $q->where('statut', $statut))
            ->orderByRaw("CASE WHEN statut = 'ouvert' THEN 0 ELSE 1 END")
            ->orderByDesc('debut_at')
            ->paginate(20)
            ->withQueryString();

        return view('incidents.index', [
            'incidents' => $incidents,
            'statut'    => $statut,
            'nbOuverts' => Incident::ouverts()->count(),
            'horloge'   => ResilienceService::horlogeBloquee(),
        ]);
    }

    public function show(Incident $incident)
    {
        $incident->load('poste', 'traitePar');

        $lignes = $incident->sessions()->with('poste')->orderBy('poste_id')->get()->map(fn (Session $s) => [
            'session'   => $s,
            'gelee'     => SessionTransparence::estSuspendue($s),
            'conserve'  => SessionTransparence::tempsConserve($s),
            'montant'   => $this->sessions->montant($s),
            'trace'     => SessionTransparence::pour($s),
        ]);

        return view('incidents.show', [
            'incident'     => $incident,
            'lignes'       => $lignes,
            'nbGelees'     => $lignes->where('gelee', true)->count(),
            'horloge'      => ResilienceService::horlogeBloquee(),
            'peutDecider'  => $this->peutDecider(),
        ]);
    }

    public function bandeau(): JsonResponse
    {
        return response()->json(BandeauIncidents::donnees());
    }

    public function reprendreTout(Incident $incident): RedirectResponse
    {
        return $this->repondre($incident, $this->decision->appliquer($incident, Decision::REPRENDRE, null, auth()->id()));
    }

    public function cloturerTout(Incident $incident): RedirectResponse
    {
        return $this->repondre($incident, $this->decision->appliquer($incident, Decision::CLOTURER, null, auth()->id()));
    }

    public function reprendreSession(Incident $incident, Session $session): RedirectResponse
    {
        abort_unless($session->incident_id === $incident->id, 404);

        return $this->repondre($incident, $this->decision->appliquer($incident, Decision::REPRENDRE, $session->id, auth()->id()));
    }

    public function cloturerSession(Incident $incident, Session $session): RedirectResponse
    {
        abort_unless($session->incident_id === $incident->id, 404);

        return $this->repondre($incident, $this->decision->appliquer($incident, Decision::CLOTURER, $session->id, auth()->id()));
    }

    public function acquitter(Incident $incident): RedirectResponse
    {
        $erreur = $this->decision->acquitter($incident, auth()->id());

        return redirect()->route('incidents.show', $incident)
            ->with($erreur ? 'erreur' : 'succes', $erreur ?? 'Incident acquitté.');
    }

    public function confirmerHorloge(): RedirectResponse
    {
        $this->resilience->confirmerHorloge();

        return back()->with('succes', 'Heure confirmée : les automatismes sont rétablis.');
    }

    private function repondre(Incident $incident, array $bilan): RedirectResponse
    {
        $msg = "{$bilan['ok']} session(s) traitée(s)."
             . ($bilan['ignorees'] ? " {$bilan['ignorees']} déjà traitée(s) ailleurs." : '')
             . ($bilan['incident_clos'] ? ' Incident clos automatiquement.' : '');

        $r = redirect()->route('incidents.show', $incident)->with($bilan['ok'] > 0 || !$bilan['erreurs'] ? 'succes' : 'erreur', $msg);

        return $bilan['erreurs'] ? $r->with('detail_erreurs', $bilan['erreurs']) : $r;
    }
}