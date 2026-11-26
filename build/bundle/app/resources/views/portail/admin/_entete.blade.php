<div class="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
    <div class="min-w-0">
        <div class="flex flex-wrap items-center gap-2">
            <h1 class="text-lg font-bold tracking-tight text-slate-900 sm:text-xl dark:text-white">Portail Captif Wi-Fi</h1>

            @if ($routeur['connecte'])
                <span class="inline-flex items-center gap-1.5 rounded-full border border-emerald-200 bg-emerald-50 px-2 py-0.5 text-[11px] font-semibold text-emerald-800">
                    <span class="h-1.5 w-1.5 rounded-full bg-emerald-500"></span>
                    MikroTik connecté {{ $routeur['identity'] ? '· ' . $routeur['identity'] : '' }}
                </span>
            @elseif (!empty($routeur['configure']))
                <span class="inline-flex items-center gap-1.5 rounded-full border border-rose-200 bg-rose-50 px-2 py-0.5 text-[11px] font-semibold text-rose-800"
                      title="{{ $routeur['erreur'] }}">
                    <span class="h-1.5 w-1.5 rounded-full bg-rose-500"></span>
                    MikroTik injoignable
                </span>
            @else
                <span class="inline-flex items-center gap-1.5 rounded-full border border-slate-200 bg-slate-100 px-2 py-0.5 text-[11px] font-semibold text-slate-600">
                    <span class="h-1.5 w-1.5 rounded-full bg-slate-400"></span>
                    Mode sans routeur (PC fixes)
                </span>
            @endif

            <span x-show="dirty" x-cloak class="inline-flex items-center gap-1 rounded-full border border-amber-300 bg-amber-50 px-2 py-0.5 text-[11px] font-semibold text-amber-800">
                <span class="h-1.5 w-1.5 animate-ping rounded-full bg-amber-500"></span> Non enregistré
            </span>
        </div>
        <p class="text-xs text-slate-500 dark:text-slate-400">Écran d'accueil affiché sur les téléphones lors de leur connexion au Wi-Fi.</p>
    </div>

    <div class="flex shrink-0 items-center gap-2">
        <button type="button" x-show="dirty" x-cloak @click="annuler()"
                class="rounded-lg border border-slate-300 bg-white px-2.5 py-1 text-xs font-medium text-slate-600 hover:bg-slate-50">
            Annuler
        </button>

        <a href="{{ route('portail.login') }}" target="_blank" rel="noopener"
           class="inline-flex items-center gap-1.5 rounded-lg border border-slate-300 bg-white px-3 py-1 text-xs font-medium text-slate-700 shadow-2xs hover:bg-slate-50 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-200">
            <x-icone nom="external-link" class="h-3.5 w-3.5" /> Voir la page client
        </a>
    </div>
</div>