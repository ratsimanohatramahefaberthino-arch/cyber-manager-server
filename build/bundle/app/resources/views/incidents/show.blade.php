@extends('layouts.cyber-manager', ['title' => 'Incident — Cyber Manager'])

@section('content')
@php use App\Support\Heure; @endphp
<div class="mx-auto max-w-[1200px] space-y-5">
    <a href="{{ route('incidents.index') }}" class="text-sm font-semibold text-indigo-600 hover:underline">← Tous les incidents</a>

    <div class="flex flex-wrap items-start justify-between gap-3">
        <div>
            <h1 class="text-2xl font-semibold tracking-tight text-slate-900">{{ $incident->libelleType() }}</h1>
            <p class="text-sm text-slate-500">
                Du {{ Heure::local($incident->debut_at, 'd/m/Y H:i:s') }}
                @if($incident->fin_at) au {{ Heure::local($incident->fin_at, 'd/m/Y H:i:s') }} @endif
                @if($incident->poste) — poste {{ $incident->poste->nom_poste }} @endif
            </p>
        </div>
        <span class="rounded-full px-3 py-1 text-xs font-bold {{ $incident->estOuvert() ? 'bg-amber-100 text-amber-800' : 'bg-emerald-100 text-emerald-800' }}">
            {{ $incident->estOuvert() ? 'À traiter' : 'Traité' }}
        </span>
    </div>

    @include('incidents._flash')

    <div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <div class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <div class="text-xs font-bold uppercase tracking-wider text-slate-400">Durée de la coupure</div>
            <div class="mt-2 text-2xl font-extrabold text-slate-900">{{ Heure::duree($incident->duree_secondes) }}</div>
            <p class="mt-1 text-xs text-slate-500">Non facturée au client.</p>
        </div>
        <div class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <div class="text-xs font-bold uppercase tracking-wider text-slate-400">Sessions interrompues</div>
            <div class="mt-2 text-2xl font-extrabold text-slate-900">{{ $incident->nb_sessions_figees }}</div>
            <p class="mt-1 text-xs text-slate-500">{{ $nbGelees }} encore gelée(s).</p>
        </div>
        <div class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <div class="text-xs font-bold uppercase tracking-wider text-slate-400">Montant arrêté</div>
            <div class="mt-2 text-2xl font-extrabold text-slate-900">{{ number_format($incident->montant_fige_total, 0, ',', ' ') }} <span class="text-base text-emerald-600">Ar</span></div>
            <p class="mt-1 text-xs text-slate-500">Politique appliquée : {{ $incident->politique_appliquee ?? '—' }}.</p>
        </div>
    </div>

    @if($incident->type === \App\Models\Incident::TYPE_HORLOGE)
        <div class="rounded-2xl border border-rose-200 bg-rose-50 p-5 text-sm text-rose-900">
            <p>L'heure du serveur a fait un saut anormal{{ isset($incident->details['ecart_secondes']) ? ' (écart de ' . Heure::duree(abs((int) $incident->details['ecart_secondes'])) . ')' : '' }}.
               Par sécurité, rien n'a été gelé ni clôturé.</p>
            <p class="mt-1">Heure du serveur maintenant : <strong>{{ Heure::local(now(), 'd/m/Y H:i:s') }}</strong>.</p>
            @if($horloge && $peutDecider)
                <form method="POST" action="{{ route('incidents.horloge.confirmer') }}" class="mt-3" onsubmit="return confirm('Confirmer que l\'heure affichée est correcte ?')">
                    @csrf <button class="rounded-lg bg-rose-600 px-4 py-2 font-bold text-white hover:bg-rose-700">L'heure est correcte, continuer</button>
                </form>
            @endif
        </div>
    @endif

    @if($lignes->isNotEmpty())
    <div class="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
        <div class="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 bg-slate-50 px-5 py-3">
            <h2 class="font-bold text-slate-800">Sessions concernées</h2>
            @if($nbGelees > 0 && $peutDecider)
                <div class="flex flex-wrap gap-2">
                    <form method="POST" action="{{ route('incidents.reprendre-tout', $incident) }}" onsubmit="return confirm('Reprendre toutes les sessions gelées dont le poste est revenu ?')">
                        @csrf <button class="rounded-lg bg-emerald-600 px-3 py-1.5 text-xs font-bold text-white hover:bg-emerald-700">Tout reprendre</button>
                    </form>
                    <form method="POST" action="{{ route('incidents.cloturer-tout', $incident) }}" onsubmit="return confirm('Clôturer toutes les sessions gelées au montant arrêté ? Cette action est définitive.')">
                        @csrf <button class="rounded-lg bg-rose-600 px-3 py-1.5 text-xs font-bold text-white hover:bg-rose-700">Tout clôturer au montant arrêté</button>
                    </form>
                </div>
            @endif
        </div>
        <div class="overflow-x-auto">
            <table class="w-full text-sm">
                <thead class="text-left text-xs uppercase tracking-wider text-slate-500">
                    <tr><th class="px-4 py-3">Poste</th><th class="px-4 py-3">Temps conservé</th><th class="px-4 py-3">Montant arrêté</th><th class="px-4 py-3">Traçabilité</th><th class="px-4 py-3 text-right">Décision</th></tr>
                </thead>
                <tbody class="divide-y divide-slate-100">
                @foreach($lignes as $l)
                    <tr>
                        <td class="px-4 py-3 font-medium text-slate-900">{{ $l['session']->poste?->nom_poste ?? '—' }}</td>
                        <td class="whitespace-nowrap px-4 py-3 font-mono">{{ Heure::duree($l['conserve']) }}</td>
                        <td class="whitespace-nowrap px-4 py-3 font-semibold">{{ number_format($l['montant'], 0, ',', ' ') }} Ar</td>
                        <td class="px-4 py-3 text-xs text-slate-600">
                            @include('incidents._tracabilite', ['trace' => $l['trace']])
                        </td>
                        <td class="px-4 py-3 text-right">
                            @if(!$l['gelee'])
                                <span class="text-xs font-semibold text-slate-400">{{ $l['session']->etat === 'terminee' ? 'Clôturée' : 'Reprise' }}</span>
                            @elseif($peutDecider)
                                <div class="flex justify-end gap-2">
                                    <form method="POST" action="{{ route('incidents.session.reprendre', [$incident, $l['session']]) }}">@csrf
                                        <button class="rounded-lg border border-emerald-300 px-2.5 py-1 text-xs font-bold text-emerald-700 hover:bg-emerald-50">Reprendre</button></form>
                                    <form method="POST" action="{{ route('incidents.session.cloturer', [$incident, $l['session']]) }}" onsubmit="return confirm('Clôturer cette session au montant arrêté ?')">@csrf
                                        <button class="rounded-lg border border-rose-300 px-2.5 py-1 text-xs font-bold text-rose-700 hover:bg-rose-50">Clôturer</button></form>
                                </div>
                            @else
                                <span class="text-xs text-slate-400">Décision du gérant</span>
                            @endif
                        </td>
                    </tr>
                @endforeach
                </tbody>
            </table>
        </div>
    </div>
    @endif

    @if($incident->estOuvert() && $peutDecider)
        <form method="POST" action="{{ route('incidents.acquitter', $incident) }}" class="text-right">@csrf
            <button class="rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm font-semibold text-slate-700 hover:bg-slate-50">Acquitter l'incident</button>
            <p class="mt-1 text-xs text-slate-400">Possible seulement quand plus aucune session n'est gelée.</p>
        </form>
    @elseif(!$incident->estOuvert())
        <p class="text-sm text-slate-500">Traité le {{ Heure::local($incident->traite_at) }}{{ $incident->traitePar ? ' par ' . $incident->traitePar->name : '' }}.</p>
    @endif
</div>
@endsection