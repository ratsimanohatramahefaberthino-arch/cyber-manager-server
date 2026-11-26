@extends('layouts.cyber-manager', ['title' => 'Résilience — Cyber Manager'])

@section('content')
@php
    $champ = function (string $cle, string $titre, string $unite, string $aide) use ($valeurs, $defs) {
        $d = $defs[$cle];
        return compact('cle', 'titre', 'unite', 'aide', 'd') + ['val' => old($cle, $valeurs[$cle])];
    };
    $groupes = [
        'Détection d\'une coupure' => [
            $champ('delai_injoignable_secondes', 'Un poste est déclaré « coupé » après', 'secondes', 'Sans nouvelles du poste pendant ce temps, sa session est gelée au dernier signe de vie et son Internet est coupé.'),
            $champ('delai_grace_agent_secondes', 'Délai de grâce de l\'agent', 'secondes', 'Patience laissée au programme du poste avant de se considérer isolé du serveur (micro-coupures Wi-Fi ou câble).'),
        ],
        'Après le retour du courant' => [
            $champ('fenetre_reconnexion_minutes', 'Fenêtre de reconnexion', 'minutes', 'Après un redémarrage du serveur, aucune clôture automatique pendant ce temps : les PC mettent du temps à se rallumer.'),
            $champ('abandon_apres_retour_minutes', 'Abandon si le client ne reprend pas, après retour du poste', 'minutes', 'Passé ce délai, la session gelée est clôturée au montant arrêté. 0 = jamais d\'abandon automatique.'),
            $champ('abandon_poste_absent_minutes', 'Abandon si le poste ne revient jamais', 'minutes', 'Passé ce délai, la session gelée est clôturée au montant arrêté. 0 = jamais.'),
        ],
    ];
    $politiques = [
        'client' => ['Le client décide', 'Au retour du courant, le client choisit sur son poste de reprendre ou de terminer. Le plus simple pour vous.'],
        'gerant' => ['Je décide', 'Rien ne bouge tant que vous n\'avez pas tranché dans la page Incidents. Le plus de contrôle.'],
        'auto'   => ['Automatique', 'Le système reprend tout seul quand le poste revient, et clôture selon les délais d\'abandon ci-dessous. Le moins de travail.'],
    ];
@endphp

<div class="mx-auto max-w-3xl space-y-5">
    <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div>
            <h1 class="text-2xl font-semibold tracking-tight text-slate-900">Coupures de courant</h1>
            <p class="text-sm text-slate-500">Quoi qu'il arrive, le client ne paie jamais le temps passé dans le noir. Ici, vous réglez ce qui se passe ensuite.</p>
        </div>
        <a href="{{ route('incidents.index') }}"
           class="inline-flex items-center gap-2 rounded-xl border border-slate-300 bg-white px-4 py-2 text-sm font-semibold text-slate-700 shadow-sm transition hover:bg-slate-50">
            <span>⚡</span>
            <span>Voir l'historique des incidents →</span>
        </a>
    </div>

    @include('incidents._flash')

    <form method="POST" action="{{ route('parametres.resilience.update') }}" class="space-y-5">
        @csrf

        <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <h2 class="font-bold text-slate-900">Que faire d'une session interrompue ?</h2>
            <div class="mt-3 grid gap-3 sm:grid-cols-3">
                @foreach($politiques as $cle => [$titre, $texte])
                    <label class="cursor-pointer rounded-xl border p-4 text-sm transition has-[:checked]:border-indigo-500 has-[:checked]:bg-indigo-50 border-slate-200 hover:bg-slate-50">
                        <input type="radio" name="politique_reprise" value="{{ $cle }}" class="sr-only" @checked(old('politique_reprise', $valeurs['politique_reprise']) === $cle)>
                        <span class="block font-bold text-slate-900">{{ $titre }}</span>
                        <span class="mt-1 block text-xs text-slate-500">{{ $texte }}</span>
                    </label>
                @endforeach
            </div>
        </section>

        @foreach($groupes as $titreGroupe => $champs)
        <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <h2 class="font-bold text-slate-900">{{ $titreGroupe }}</h2>
            <div class="mt-3 space-y-5">
                @foreach($champs as $c)
                    <div x-data="{ v: {{ (int) $c['val'] }} }">
                        <label class="text-sm font-semibold text-slate-800" for="{{ $c['cle'] }}">{{ $c['titre'] }}</label>
                        <div class="mt-1 flex items-center gap-2">
                            <input id="{{ $c['cle'] }}" name="{{ $c['cle'] }}" type="number" x-model.number="v"
                                   min="{{ $c['d']['min'] }}" max="{{ $c['d']['max'] }}" step="1" required
                                   class="w-28 rounded-lg border-slate-300 text-sm @error($c['cle']) border-rose-500 @enderror">
                            <span class="text-sm text-slate-500">{{ $c['unite'] }}</span>
                            @if($c['unite'] === 'secondes')
                                <span class="text-xs text-slate-400" x-text="v >= 60 ? '= ' + (v / 60).toFixed(1).replace('.0','') + ' min' : ''"></span>
                            @endif
                        </div>
                        <p class="mt-1 text-xs text-slate-500">{{ $c['aide'] }}</p>
                        <p class="text-[11px] text-slate-400">Entre {{ $c['d']['min'] }} et {{ $c['d']['max'] }} — recommandé : {{ $c['d']['defaut'] }}.</p>
                    </div>
                @endforeach
            </div>
        </section>
        @endforeach

        <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <h2 class="font-bold text-slate-900">Sécurité de l'horloge</h2>
            <label class="mt-3 flex items-start gap-3 text-sm">
                <input type="hidden" name="horloge_garde_active" value="0">
                <input type="checkbox" name="horloge_garde_active" value="1" class="mt-1 rounded border-slate-300" @checked(old('horloge_garde_active', $valeurs['horloge_garde_active']))>
                <span><strong>Me prévenir si l'heure du serveur saute</strong> (pile CMOS à plat, mauvaise date au démarrage).<br>
                <span class="text-xs text-slate-500">Recommandé. Si l'heure est fausse, rien n'est gelé ni clôturé tant que vous n'avez pas confirmé.</span></span>
            </label>
        </section>

        <details class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <summary class="cursor-pointer font-bold text-slate-900">Réglage avancé</summary>
            <div class="mt-3">
                <label class="text-sm font-semibold text-slate-800" for="port_decouverte_udp">Port de découverte des postes</label>
                <input id="port_decouverte_udp" name="port_decouverte_udp" type="number" required
                       min="{{ $defs['port_decouverte_udp']['min'] }}" max="{{ $defs['port_decouverte_udp']['max'] }}"
                       value="{{ old('port_decouverte_udp', $valeurs['port_decouverte_udp']) }}" class="mt-1 block w-28 rounded-lg border-slate-300 text-sm">
                <p class="mt-1 text-xs text-amber-700">À ne changer que si un autre logiciel utilise déjà ce port (recommandé : {{ $defs['port_decouverte_udp']['defaut'] }}). Les postes devront être mis à jour pour suivre.</p>
            </div>
        </details>

        <div class="flex items-center justify-between">
            <button type="submit" form="form-reset" class="text-sm font-semibold text-slate-500 hover:text-slate-800"
                    onclick="return confirm('Remettre tous les réglages aux valeurs recommandées ?')">Valeurs recommandées</button>
            <button class="rounded-lg bg-indigo-600 px-5 py-2.5 text-sm font-bold text-white hover:bg-indigo-700">Enregistrer</button>
        </div>
    </form>

    <form id="form-reset" method="POST" action="{{ route('parametres.resilience.reinitialiser') }}" class="hidden">@csrf</form>
</div>
@endsection