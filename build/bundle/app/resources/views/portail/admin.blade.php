@extends('layouts.cyber-manager', ['title' => 'Portail Captif — Cyber Manager'])

@section('content')
@php
    $clesGeneral = ['titre', 'titre_taille', 'message', 'titre_couleur', 'message_couleur', 'annonce_texte', 'annonce_fond', 'annonce_texte_color', 'footer_texte', 'logo'];
    $clesDesign  = ['police', 'fond_type', 'fond_couleur_1', 'fond_couleur_2', 'carte_couleur', 'carte_texte', 'bouton_fond', 'bouton_texte', 'bouton_rayon', 'accent_couleur'];
    $clesReseau  = ['portail_domaine', 'portail_ip_serveur', 'mikrotik_host', 'mikrotik_port', 'mikrotik_user', 'mikrotik_password'];

    $onglet = session('onglet', 'general');
    if ($errors->any()) {
        $onglet = $errors->hasAny($clesGeneral) ? 'general' : ($errors->hasAny($clesDesign) ? 'design' : ($errors->hasAny($clesReseau) ? 'reseau' : 'general'));
    }
    if (!in_array($onglet, ['general', 'design', 'reseau'], true)) {
        $onglet = 'general';
    }

    $form = [];
    foreach (\App\Services\PortailConfig::CHAMPS as $cle) {
        $form[$cle] = old($cle, $config[$cle]);
    }
    $form['titre_taille']    = (int) $form['titre_taille'];
    $form['bouton_rayon']    = (int) $form['bouton_rayon'];
    $form['annonce_active']  = (string) $form['annonce_active'] === '1';
    $form['portail_domaine'] = old('portail_domaine', $domaine);

    $payload = [
        'tab'     => $onglet,
        'form'    => $form,
        'logoUrl' => $logoUrl,
        'tarif'   => $tarifTexte,
        'port'    => $port,
    ];

    $champ = 'w-full rounded-lg border-slate-300 bg-white text-sm text-slate-900 shadow-sm placeholder:text-slate-400 focus:border-violet-500 focus:ring-violet-500 dark:border-slate-700 dark:bg-slate-900 dark:text-white';
    $label = 'mb-1.5 block text-[13px] font-medium text-slate-700 dark:text-slate-300';
    $bouton2 = 'inline-flex items-center justify-center gap-1.5 rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-xs font-semibold text-slate-700 shadow-xs hover:bg-slate-50 focus:outline-none dark:border-slate-700 dark:bg-slate-800 dark:text-slate-200';
@endphp

<script>
document.addEventListener('alpine:init', () => {
    Alpine.data('studioPortail', (init) => ({
        tab: init.tab,
        mode: 'mobile',
        envoi: false,
        partant: false,
        form: init.form,
        initial: '',
        logoUrl: init.logoUrl,
        logoInitial: init.logoUrl,
        logoNom: '',
        logoErreur: '',
        supprimerLogo: false,
        tarif: init.tarif,
        port: init.port,
        echelle: 1,
        detectionEnCours: false,
        detectionMessage: '',
        detectionStatut: null,

        get cadre() {
            return this.mode === 'mobile' ? { w: 399, h: 764 } : { w: 1000, h: 680 };
        },
        get rayonPreset() {
            const r = Number(this.form.bouton_rayon);
            return r <= 8 ? 'carre' : (r >= 40 ? 'pilule' : 'doux');
        },
        get urlAffichee() {
            return 'http://' + (this.form.portail_domaine || 'portail') + (this.port !== 80 ? ':' + this.port : '') + '/portail';
        },
        get fondEcran() {
            const f = this.form;
            return {
                fontFamily: f.police,
                background: f.fond_type === 'gradient'
                    ? 'radial-gradient(120% 90% at 50% 0, ' + f.fond_couleur_2 + ' 0, transparent 70%), ' + f.fond_couleur_1
                    : f.fond_couleur_1,
            };
        },
        get dirty() {
            return JSON.stringify(this.form) !== this.initial || this.logoNom !== '' || this.supprimerLogo;
        },

        init() {
            this.initial = JSON.stringify(this.form);
            this.$nextTick(() => {
                this.ajuster();
                if (window.ResizeObserver && this.$refs.scene) {
                    new ResizeObserver(() => this.ajuster()).observe(this.$refs.scene);
                }
            });
            this.$watch('mode', () => this.$nextTick(() => this.ajuster()));
            window.addEventListener('beforeunload', (e) => {
                if (this.dirty && !this.partant) { e.preventDefault(); e.returnValue = ''; }
            });
        },

        ajuster() {
            const s = this.$refs.scene;
            if (!s || !s.clientWidth) return;
            this.echelle = Math.min(1, s.clientWidth / this.cadre.w);
        },

        async scannerRouteur() {
            this.detectionEnCours = true;
            this.detectionMessage = 'Recherche automatique du routeur sur le réseau local...';
            this.detectionStatut = 'info';

            try {
                const res = await window.cmFetch("{{ route('portail.admin.detecter') }}");
                const data = await res.json();

                if (data.succes && data.passerelle) {
                    const hostInput = document.getElementById('mt-host');
                    const portInput = document.getElementById('mt-port');
                    const userInput = document.getElementById('mt-user');

                    if (hostInput) hostInput.value = data.passerelle;
                    if (portInput && !portInput.value) portInput.value = '8728';
                    if (userInput && !userInput.value) userInput.value = 'admin';

                    this.detectionStatut = data.port_api_ouvert ? 'succes' : 'avertissement';
                    this.detectionMessage = data.message;
                } else {
                    this.detectionStatut = 'erreur';
                    this.detectionMessage = data.message || 'Impossible de trouver le routeur.';
                }
            } catch (e) {
                this.detectionStatut = 'erreur';
                this.detectionMessage = 'Erreur lors du scan réseau.';
            } finally {
                this.detectionEnCours = false;
            }
        },

        changerLogo(e) {
            const f = e.target.files[0];
            if (!f) return;
            if (f.size > 2 * 1024 * 1024) {
                this.logoErreur = 'Image trop lourde (2 Mo maximum).';
                e.target.value = '';
                return;
            }
            this.logoErreur = '';
            this.logoNom = f.name;
            this.logoUrl = URL.createObjectURL(f);
            this.supprimerLogo = false;
        },

        retirerLogo() {
            this.logoUrl = '';
            this.logoNom = '';
            this.logoErreur = '';
            this.supprimerLogo = true;
            if (this.$refs.logoInput) this.$refs.logoInput.value = '';
        },

        annuler() {
            this.form = JSON.parse(this.initial);
            this.logoUrl = this.logoInitial;
            this.logoNom = '';
            this.logoErreur = '';
            this.supprimerLogo = false;
            if (this.$refs.logoInput) this.$refs.logoInput.value = '';
        },
    }));
});
</script>

<form id="form-portail" method="POST" action="{{ route('portail.admin.save') }}" enctype="multipart/form-data" novalidate
      x-data="studioPortail(@js($payload))"
      @submit="partant = true; setTimeout(() => envoi = true, 0)"
      @pageshow.window="envoi = false"
      class="space-y-4">
    @csrf
    <input type="hidden" name="onglet" :value="tab">
    <input type="hidden" name="supprimer_logo" :value="supprimerLogo ? '1' : '0'">
    <input type="hidden" name="annonce_active" :value="form.annonce_active ? '1' : '0'">

    {{-- En-tête principal : Titre + Statut (scroll naturel avec la page) --}}
    @include('portail.admin._entete')

    <x-flash />

    @if ($errors->any())
        <div class="rounded-xl border border-rose-200 bg-rose-50 px-4 py-2.5 text-xs text-rose-800">
            <p class="font-semibold">Veuillez corriger les informations suivantes :</p>
            <ul class="mt-1 list-disc space-y-0.5 pl-5">
                @foreach ($errors->all() as $e)
                    <li>{{ $e }}</li>
                @endforeach
            </ul>
        </div>
    @endif

    @if ($sync['ip_changee'])
        <div class="rounded-xl border border-amber-200 bg-amber-50 px-3.5 py-2 text-xs text-amber-900">
            <strong class="font-semibold">L'adresse IP de ce PC a changé :</strong>
            cliquez sur <strong>« Diffuser au routeur »</strong> pour que vos clients continuent d'être redirigés.
        </div>
    @endif

    {{-- BARRE COMPACTE UNIQUE (STICKY) : Vrais onglets soulignés à gauche + Boutons d'action à droite --}}
    <div class="sticky top-0 z-30 -mx-4 bg-slate-50/95 px-4 py-1.5 backdrop-blur-md border-b border-slate-200 sm:-mx-6 sm:px-6 dark:bg-slate-950/95 dark:border-slate-800">
        <div class="flex flex-wrap items-center justify-between gap-2">

            {{-- VRAIS ONGLETS WEB : discrets, élégants, soulignés d'un trait violet --}}
            <nav class="flex items-center space-x-2 sm:space-x-5" aria-label="Sous-onglets">
                @foreach ([['general', 'Général & Textes', 'pencil'], ['design', 'Design & Couleurs', 'palette'], ['reseau', 'Réseau & Routeur', 'wifi']] as [$id, $labelTexte, $ic])
                    <button type="button" role="tab" @click="tab = '{{ $id }}'"
                            :class="tab === '{{ $id }}'
                                ? 'border-violet-600 text-violet-700 font-bold dark:border-violet-400 dark:text-violet-300'
                                : 'border-transparent text-slate-500 hover:text-slate-800 hover:border-slate-300 dark:text-slate-400 dark:hover:text-slate-200'"
                            class="inline-flex items-center gap-1.5 border-b-2 py-2 px-1 text-xs transition focus:outline-none">
                        <x-icone :nom="$ic" class="h-3.5 w-3.5" />
                        <span>{{ $labelTexte }}</span>
                    </button>
                @endforeach
            </nav>

            {{-- BOUTONS D'ACTION COMPACTS : 1 seule ligne, claire et aérée --}}
            <div class="flex items-center gap-2">
                <button type="submit" :disabled="envoi" class="{{ $bouton2 }}"
                        title="Enregistre dans Cyber Manager. Visible tout de suite sur la page d'accueil sans toucher au routeur.">
                    <x-icone nom="check" class="h-3.5 w-3.5 text-emerald-600" />
                    <span>Enregistrer</span>
                </button>

                <button type="submit" name="diffuser" value="1" :disabled="envoi"
                        class="inline-flex items-center justify-center gap-1.5 rounded-lg bg-violet-600 px-3 py-1.5 text-xs font-semibold text-white shadow-xs transition hover:bg-violet-700 focus:outline-none disabled:opacity-60"
                        title="Enregistre ET met à jour la redirection sur votre boîtier MikroTik.">
                    <x-icone nom="upload-cloud" class="h-3.5 w-3.5" />
                    <span x-text="envoi ? 'Envoi...' : 'Diffuser au routeur'">Diffuser au routeur</span>
                </button>
            </div>
        </div>
    </div>

    {{-- Formulaire et Aperçu --}}
    <div class="grid grid-cols-1 items-start gap-5 2xl:grid-cols-12 pt-1">
        <div class="min-w-0 space-y-5 2xl:col-span-7" x-ref="haut">
            @include('portail.admin._general')
            @include('portail.admin._design')
            @include('portail.admin._reseau')
        </div>

        <div class="min-w-0 2xl:sticky 2xl:top-14 2xl:col-span-5" x-ref="apercu">
            @include('portail.admin._apercu')
        </div>
    </div>

    {{-- Bouton aperçu mobile --}}
    <button type="button" @click="$refs.apercu.scrollIntoView({ behavior: 'smooth', block: 'start' })"
            class="fixed bottom-4 right-4 z-40 inline-flex items-center gap-1.5 rounded-full bg-violet-600 px-3.5 py-2 text-xs font-semibold text-white shadow-lg 2xl:hidden">
        <x-icone nom="eye" class="h-4 w-4" /> Aperçu
    </button>
</form>

<form id="form-reset" method="POST" action="{{ route('portail.admin.reset') }}">@csrf</form>
@endsection