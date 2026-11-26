@extends('layouts.cyber-manager', ['title' => 'Incidents — Cyber Manager'])

@section('content')
@php use App\Support\Heure; @endphp
<div class="mx-auto max-w-[1200px] space-y-5">
    <div class="flex flex-wrap items-end justify-between gap-3">
        <div>
            <h1 class="text-2xl font-semibold tracking-tight text-slate-900">Incidents</h1>
            <p class="text-sm text-slate-500">Coupures de courant et anomalies d'horloge. Aucun temps d'obscurité n'est jamais facturé au client.</p>
        </div>
        <div class="flex gap-2 text-sm">
            @foreach(['ouvert' => 'À traiter (' . $nbOuverts . ')', 'traite' => 'Traités', 'tous' => 'Tous'] as $cle => $lib)
                <a href="{{ route('incidents.index', ['statut' => $cle]) }}"
                   class="rounded-lg px-3 py-1.5 font-semibold {{ $statut === $cle ? 'bg-indigo-600 text-white' : 'border border-slate-200 bg-white text-slate-600 hover:bg-slate-50' }}">{{ $lib }}</a>
            @endforeach
        </div>
    </div>

    @include('incidents._flash')

    @if($horloge)
        <div class="rounded-2xl border border-rose-200 bg-rose-50 p-5">
            <h2 class="font-bold text-rose-900">L'heure du serveur est suspecte</h2>
            <p class="mt-1 text-sm text-rose-800">Heure actuelle du serveur : <strong>{{ Heure::local(now(), 'd/m/Y H:i:s') }}</strong>. Comparez avec l'heure de votre téléphone.</p>
            @if(auth()->user()?->role === 'admin')
                <form method="POST" action="{{ route('incidents.horloge.confirmer') }}" class="mt-3"
                      onsubmit="return confirm('Confirmer que l\'heure affichée est correcte ?')">
                    @csrf
                    <button class="rounded-lg bg-rose-600 px-4 py-2 text-sm font-bold text-white hover:bg-rose-700">L'heure est correcte, continuer</button>
                </form>
            @endif
        </div>
    @endif

    <div class="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
        <div class="overflow-x-auto">
            <table class="w-full text-sm">
                <thead class="bg-slate-50 text-left text-xs uppercase tracking-wider text-slate-500">
                    <tr>
                        <th class="px-4 py-3">Quand</th><th class="px-4 py-3">Type</th><th class="px-4 py-3">Durée</th>
                        <th class="px-4 py-3">Sessions</th><th class="px-4 py-3">Montant arrêté</th><th class="px-4 py-3">État</th><th></th>
                    </tr>
                </thead>
                <tbody class="divide-y divide-slate-100">
                @forelse($incidents as $i)
                    <tr class="hover:bg-slate-50">
                        <td class="whitespace-nowrap px-4 py-3">{{ Heure::local($i->debut_at) }}</td>
                        <td class="px-4 py-3 font-medium text-slate-900">
                            {{ $i->libelleType() }}
                            @if($i->poste)<span class="block text-xs font-normal text-slate-500">{{ $i->poste->nom_poste }}</span>@endif
                        </td>
                        <td class="whitespace-nowrap px-4 py-3">{{ Heure::duree($i->duree_secondes) }}</td>
                        <td class="px-4 py-3">{{ $i->nb_sessions_figees }}</td>
                        <td class="whitespace-nowrap px-4 py-3 font-semibold">{{ number_format($i->montant_fige_total, 0, ',', ' ') }} Ar</td>
                        <td class="px-4 py-3">
                            <span class="rounded-full px-2.5 py-0.5 text-xs font-bold {{ $i->estOuvert() ? 'bg-amber-100 text-amber-800' : 'bg-slate-100 text-slate-600' }}">
                                {{ $i->estOuvert() ? 'À traiter' : 'Traité' }}
                            </span>
                        </td>
                        <td class="px-4 py-3 text-right"><a href="{{ route('incidents.show', $i) }}" class="font-semibold text-indigo-600 hover:underline">Détail →</a></td>
                    </tr>
                @empty
                    <tr><td colspan="7" class="px-4 py-10 text-center text-slate-400">Aucun incident. Tout fonctionne normalement.</td></tr>
                @endforelse
                </tbody>
            </table>
        </div>
    </div>

    {{ $incidents->links() }}
</div>
@endsection