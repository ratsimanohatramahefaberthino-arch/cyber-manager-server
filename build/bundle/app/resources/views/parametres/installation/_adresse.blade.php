<section class="rounded-2xl border border-violet-100 bg-gradient-to-r from-violet-50 to-slate-50 p-5 shadow-sm sm:p-6">
    <div class="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
        <div class="min-w-0 space-y-2">
            <p class="text-xs font-bold uppercase tracking-wider text-violet-700">Adresse de ce serveur pour les postes</p>
            <p class="break-all font-mono text-xl font-extrabold text-slate-900 sm:text-2xl">{{ $url }}</p>
            <p class="text-sm text-slate-600">
                Cette adresse est déjà écrite dans le fichier à télécharger : rien à saisir sur les postes.
            </p>
        </div>

        <button type="button" @click="cmCopy(@js($url))"
                class="inline-flex shrink-0 items-center justify-center gap-2 rounded-xl border border-slate-300 bg-white px-4 py-2.5 text-sm font-semibold text-slate-800 shadow-sm transition hover:bg-slate-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-violet-500">
            <x-icone nom="copy" class="h-4 w-4" /> Copier l'adresse
        </button>
    </div>

    <div class="mt-4 space-y-2 text-sm">
        <p class="rounded-lg bg-emerald-50 px-3 py-2 text-emerald-800">
            <strong class="font-semibold">Découverte automatique active :</strong>
            Si l'adresse IP de ce serveur change un jour (changement de box, routeur redémarré), les postes clients retrouvent automatiquement ce serveur sur le réseau local sans aucune reconfiguration.
        </p>

        @if ($localhost)
            <p class="rounded-lg bg-amber-50 px-3 py-2 text-amber-800">
                Vous consultez cette page avec « localhost ». Pas de souci : le fichier téléchargé contient bien l'adresse réseau ci-dessus.
            </p>
        @endif

        @if (count($ips) > 1)
            <p class="rounded-lg bg-slate-100 px-3 py-2 text-slate-700">
                Adresses réseau disponibles : {{ implode(' · ', $ips) }}.
            </p>
        @endif
    </div>
</section>