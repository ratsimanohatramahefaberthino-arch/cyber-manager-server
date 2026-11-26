<div x-data="bandeauIncidents(@js($initial), @js(route('incidents.bandeau')))" x-init="demarrer()" x-show="d.visible" x-cloak>
    <div class="border-b px-4 py-2.5 text-sm sm:px-6"
         :class="d.horloge ? 'border-rose-200 bg-rose-50 text-rose-900' : 'border-amber-200 bg-amber-50 text-amber-900'">
        <div class="mx-auto flex max-w-[1500px] flex-wrap items-center gap-x-4 gap-y-1">
            <span class="font-bold" x-text="d.horloge ? '⚠️ L\'heure du serveur est à vérifier' : '⚡ Coupure détectée'"></span>

            <template x-if="d.horloge">
                <span>Les gels et clôtures automatiques sont suspendus tant que vous n'avez pas confirmé l'heure.</span>
            </template>
            <template x-if="!d.horloge">
                <span>
                    <strong x-text="d.nb_sessions"></strong> session(s) conservée(s) —
                    <strong x-text="fmtAr(d.montant_fige)"></strong> arrêtés —
                    coupure de <strong x-text="fmtDuree(d.duree_max_s)"></strong>
                </span>
            </template>

            <a :href="d.url || '{{ route('incidents.index') }}'" class="ml-auto rounded-lg px-3 py-1 text-xs font-bold text-white shadow-sm"
               :class="d.horloge ? 'bg-rose-600 hover:bg-rose-700' : 'bg-amber-600 hover:bg-amber-700'">
                Voir et décider →
            </a>
        </div>
    </div>
</div>

<script>
function bandeauIncidents(initial, url) {
    return {
        d: initial,
        demarrer() { setInterval(() => this.rafraichir(), 15000); },
        async rafraichir() {
            try {
                const r = await fetch(url, { headers: { 'Accept': 'application/json' } });
                if (r.ok) this.d = await r.json();
            } catch (e) { /* on garde l'affichage actuel */ }
        },
        fmtAr: n => (Number(n) || 0).toLocaleString('fr-FR') + ' Ar',
        fmtDuree(s) {
            s = Math.max(0, Math.floor(s || 0));
            if (s < 60) return s + ' s';
            if (s < 3600) return Math.floor(s / 60) + ' min';
            return Math.floor(s / 3600) + ' h ' + String(Math.floor((s % 3600) / 60)).padStart(2, '0') + ' min';
        },
    };
}
</script>