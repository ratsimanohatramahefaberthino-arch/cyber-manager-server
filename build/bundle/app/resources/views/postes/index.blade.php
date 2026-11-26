@extends('layouts.cyber-manager', ['title' => 'Postes — Cyber Manager'])

@section('content')
<style>
    /* Modales & Overlays */
    .cm-overlay { position: fixed; top: 0; right: 0; bottom: 0; left: 0; display: flex; align-items: center; justify-content: center; padding: 16px; }
    .cm-backdrop { position: absolute; top: 0; right: 0; bottom: 0; left: 0; background: rgba(15, 23, 42, .6); }
    .cm-confirm { position: relative; width: 100%; max-width: 22rem; background: #fff; border-radius: 14px; padding: 22px 20px; text-align: center; box-shadow: 0 25px 50px -12px rgba(0, 0, 0, .35); }
    .cm-confirm h3 { font-size: 16px; font-weight: 700; color: #0f172a; margin-bottom: 8px; }
    .cm-confirm p { font-size: 13px; color: #64748b; margin-bottom: 20px; }
    .cm-bascule { border: 1px solid #6366f1; background: #eef2ff; color: #4338ca; border-radius: 8px; padding: 5px 12px; font-size: 12px; font-weight: 600; cursor: pointer; transition: all .15s; white-space: nowrap; }
    .cm-bascule:hover { background: #4f46e5; color: #fff; border-color: #4f46e5; }
    .cm-err { color: #dc2626; font-size: 14px; font-weight: 700; margin-top: 8px; line-height: 1.3; }
    .cm-num { width: 5.5rem; max-width: 100%; }
    .cm-table-wrap { max-height: 13rem; overflow-y: auto; border: 1px solid #e2e8f0; border-radius: 8px; }
    .cm-table { width: 100%; table-layout: fixed; border-collapse: collapse; font-size: 14px; }
    .cm-table th { position: sticky; top: 0; background: #f1f5f9; text-align: left; padding: 8px; font-size: 12px; border-bottom: 1px solid #e2e8f0; }
    .cm-table td { padding: 8px; border-bottom: 1px solid #f1f5f9; white-space: nowrap; }

    /* Layout Postes Stable & Résistant */
    .cm-postes-container {
        display: flex;
        flex-direction: row;
        gap: 1.25rem;
        width: 100%;
        max-width: 100%;
        min-width: 0;
        align-items: stretch;
    }
    @media (max-width: 1024px) {
        .cm-postes-container {
            flex-direction: column;
        }
    }
    .cm-postes-aside {
        width: 20rem;
        min-width: 20rem;
        max-width: 20rem;
        flex-shrink: 0;
    }
    @media (max-width: 1024px) {
        .cm-postes-aside {
            width: 100%;
            min-width: 100%;
            max-width: 100%;
        }
    }
    .cm-postes-main {
        flex: 1 1 0%;
        min-width: 0;
        width: 100%;
    }
    .cm-details-grid {
        display: grid;
        grid-template-columns: minmax(0, 1.15fr) minmax(0, 1fr);
        gap: 1.5rem;
        padding: 1.5rem;
    }
    @media (max-width: 1120px) {
        .cm-details-grid {
            grid-template-columns: 1fr;
            gap: 1.25rem;
        }
    }
    .cm-info-row {
        display: flex;
        align-items: center;
        gap: 0.75rem;
        margin-bottom: 0.5rem;
    }
    .cm-info-label {
        width: 9.5rem;
        min-width: 9.5rem;
        flex-shrink: 0;
        text-align: right;
        font-size: 0.875rem;
        color: #64748b;
    }
    .cm-info-val {
        flex: 1 1 0%;
        min-width: 0;
        border-radius: 0.375rem;
        border: 1px solid #fef3c7;
        background-color: rgba(254, 243, 199, 0.7);
        padding: 0.375rem 0.75rem;
        font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
        font-size: 0.875rem;
        color: #0f172a;
    }
    .dark .cm-info-label { color: #94a3b8; }
    .dark .cm-info-val { background-color: #1e293b; border-color: #334155; color: #f8fafc; }
    .dark .cm-confirm { background-color: #1e293b; color: #f8fafc; }
    .dark .cm-confirm h3 { color: #f8fafc; }
    .dark .cm-confirm p { color: #94a3b8; }
</style>

<script>
const POSTES_URL = @json(url('/postes'));
const TARIFS_RACCOURCIS = @json($tarifs);
const TARIF_MINUTE = {{ $tarif_minute ?? 20 }};
const TARIF_MIN = {{ $tarif_min ?? 300 }};
const TARIF_ARRONDI = @json($tarif_arrondi ?? ['actif' => false, 'unite' => 0, 'seuil' => 0]);

const COULEURS = { disponible: '#22c55e', occupe: '#f59e0b', suspendu: '#8b5cf6', verrouille: '#ef4444', desactive: '#94a3b8', hors_ligne: '#94a3b8' };
const LIBELLES = { disponible: 'Disponible', occupe: 'Utilisé', suspendu: 'En pause', verrouille: 'Verrouillé', desactive: 'Désactivé', hors_ligne: 'Non présent' };
const INTERNET = { autorise: 'Autorisé', coupe: 'Coupé', erreur: 'Erreur routeur', non_gere: 'Non contrôlé (routeur non configuré)' };

function svgIcone(etat, taille = 'h-9 w-9') {
    const c = COULEURS[etat] || '#94a3b8';
    let badge = '';
    if (etat === 'hors_ligne') {
        badge = '<circle cx="31" cy="31" r="8" fill="#dc2626" stroke="#fff" stroke-width="1.5"/><rect x="30" y="26" width="2" height="6" fill="#fff"/><rect x="30" y="33" width="2" height="2" fill="#fff"/>';
    } else if (etat === 'desactive') {
        badge = '<line x1="6" y1="30" x2="30" y2="6" stroke="#475569" stroke-width="3" stroke-linecap="round"/>';
    } else if (etat === 'verrouille') {
        badge = '<rect x="26" y="25" width="10" height="8" rx="1.5" fill="#fff" stroke="#b91c1c" stroke-width="1.5"/><path d="M28 25v-3a3 3 0 0 1 6 0v3" fill="none" stroke="#b91c1c" stroke-width="1.5"/>';
    } else if (etat === 'suspendu') {
        badge = '<rect x="12" y="10" width="3.5" height="11" fill="#fff"/><rect x="20.5" y="10" width="3.5" height="11" fill="#fff"/>';
    }
    return `<svg viewBox="0 0 38 38" class="${taille}"><rect x="3" y="4" width="26" height="19" rx="2.5" fill="${c}" stroke="#1e293b" stroke-width="1.5"/><rect x="6" y="7" width="20" height="13" rx="1" fill="#0f172a" opacity=".35"/><rect x="13" y="23" width="6" height="4" fill="#64748b"/><rect x="9" y="27" width="14" height="2.5" rx="1" fill="#475569"/>${badge}</svg>`;
}

function fmtDuree(s) {
    if (s === null || s === undefined) return '—';
    s = Math.max(0, Math.floor(s));
    const p = n => String(n).padStart(2, '0');
    return `${p(Math.floor(s / 3600))}:${p(Math.floor((s % 3600) / 60))}:${p(s % 60)}`;
}
const fmtAr = n => (Number(n) || 0).toLocaleString('fr-FR') + ' Ar';

function montantPourMinutes(minutes) {
    return Math.max(TARIF_MIN, Math.max(0, Math.floor(minutes)) * TARIF_MINUTE);
}

const BOUTONS = { vert: 'bg-emerald-600 hover:bg-emerald-700 text-white', rouge: 'bg-rose-600 hover:bg-rose-700 text-white', ambre: 'bg-amber-500 hover:bg-amber-600 text-white', violet: 'bg-violet-600 hover:bg-violet-700 text-white', bleu: 'bg-sky-600 hover:bg-sky-700 text-white', gris: 'bg-slate-600 hover:bg-slate-700 text-white' };

function postesApp() {
    return {
        postes: @json($postes),
        selId: null,
        menu: { ouvert: false, x: 0, y: 0, id: null },
        modal: null,
        confirm: { ouvert: false, action: null, id: null, titre: '', texte: '', callback: null },
        form: { montantManuel: '', h: 0, m: 0 },
        lignesForfait: [...TARIFS_RACCOURCIS],
        ligneSelectionnee: null,
        newLigne: { type: 'montant', montant: '', h: 0, m: 0 },
        erreurLigne: '',
        enregLigne: false,
        hs: { username: '', password: '', voir: false, erreur: '' },
        chat: [], chatDernier: 0, chatSession: null, texte: '', envoiChat: false,
        nonLusVus: {},
        maintenant: Date.now(), recuLe: Date.now(),
        horsServeur: false, notif: null, enCours: null,

        init() {
            this.postes.forEach(p => this.nonLusVus[p.id] = p.non_lus ?? 0);
            if (this.postes.length > 0 && !this.selId) {
                this.choisir(this.postes[0].id);
            }
            setInterval(() => this.maintenant = Date.now(), 1000);
            setInterval(() => this.rafraichir(), 3000);
            document.addEventListener('click', () => this.menu.ouvert = false);
            document.addEventListener('keydown', e => {
                if (e.key === 'Escape') {
                    if (this.confirm.ouvert) { this.confirm.ouvert = false; return; }
                    this.menu.ouvert = false; this.modal = null;
                }
            });
        },

        get sel() { return this.postes.find(p => p.id === this.selId) || null; },
        get bilan() {
            const n = e => this.postes.filter(p => p.etat === e).length;
            return `${this.postes.length} poste(s) — ${n('occupe') + n('suspendu') + n('verrouille')} utilisé(s), ${n('hors_ligne')} non présent(s)`;
        },
        get totalNonLus() { return this.postes.filter(p => p.id !== this.selId).reduce((t, p) => t + (p.non_lus || 0), 0); },

        icone: svgIcone, libelle(e) { return LIBELLES[e] || e; }, mode(m) { return (m === 'forfait' ? 'Forfait' : (m === 'postpaye' ? 'Postpayé' : (m === 'libre' ? 'Session libre' : m))); },
        internet(v) { return INTERNET[v] || 'En attente'; },
        classeBouton(c) { return BOUTONS[c] || BOUTONS.gris; }, fmtAr,

        decalage(p) { if (!p.session || !['occupe', 'verrouille'].includes(p.etat)) return 0; return Math.floor((this.maintenant - this.recuLe) / 1000); },
        ecoule(p) { return p.session ? fmtDuree(p.session.ecoule_s + this.decalage(p)) : '—'; },
        restant(p) { if (!p.session || p.session.restant_s === null) return '—'; return fmtDuree(p.session.restant_s - this.decalage(p)); },
        tempsListe(p) { if (!p.session) return ''; return p.session.restant_s !== null ? this.restant(p) : this.ecoule(p); },

        montantActuel(p) {
            if (!p.session) return '—';
            if (p.session.mode === 'libre') return 'Sans facturation';
            if (p.session.etat === 'terminee') return fmtAr(p.session.montant);

            let sec = p.session.ecoule_s + this.decalage(p);
            if (p.session.mode === 'forfait' && p.session.duree_min) sec = Math.min(sec, p.session.duree_min * 60);
            return fmtAr(montantPourMinutes(Math.floor(sec / 60)));
        },

        debutAffiche(p) {
            const s = p?.session;
            if (!s) return '—';
            if (s.debut_iso) {
                const d = new Date(s.debut_iso);
                if (!isNaN(d.getTime())) return d.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
            }
            return s.debut || '—';
        },

        dureeMinimale() { return TARIF_MINUTE > 0 ? Math.ceil(TARIF_MIN / TARIF_MINUTE) : 0; },

        async rafraichir() {
            try {
                const r = await cmFetch(POSTES_URL + '/etat');
                const d = await r.json();
                for (const p of d.postes) {
                    const avant = this.nonLusVus[p.id] ?? 0;
                    if ((p.non_lus || 0) > avant && p.id !== this.selId) this.afficher(`Nouveau message de ${p.nom}`, true);
                    this.nonLusVus[p.id] = p.non_lus || 0;
                }
                this.postes = d.postes;
                this.recuLe = Date.now();
                this.horsServeur = false;
                if (this.selId && !this.sel) this.selId = null;

                if (this.selId) {
                    const sidNow = this.sel?.session?.id ?? null;
                    if (sidNow !== this.chatSession) {
                        this.chatSession = sidNow;
                        this.chat = []; this.chatDernier = 0;
                        if (sidNow && this.sel?.etat !== 'hors_ligne') await this.chargerChat(true);
                    }
                }
                if (this.selId && (this.sel?.etat === 'occupe' || this.sel?.etat === 'desactive')) {
                    this.chargerChat(false);
                }
            } catch (e) { this.horsServeur = true; }
        },

        choisir(id) {
            this.selId = id; this.chat = []; this.chatDernier = 0;
            this.chatSession = this.sel?.session?.id ?? null;
            if (this.sel?.session && this.sel?.etat !== 'hors_ligne') this.chargerChat(true);
        },

        ouvrirMenu(e, id) {
            const poste = this.postes.find(p => p.id === id);
            if (!poste || !poste.actions || poste.actions.length === 0) return;
            if (this.selId !== id) this.choisir(id);
            this.menu = { ouvert: true, x: Math.min(e.clientX, window.innerWidth - 220), y: Math.min(e.clientY, window.innerHeight - 320), id };
        },

        demanderConfirm(id, action) {
            const def = (this.postes.find(p => p.id === id)?.actions || []).find(a => a.cle === action);
            if (!def) return;
            this.menu.ouvert = false;
            if (this.selId !== id) this.choisir(id);
            if (def.modal) {
                this.form = { montantManuel: '', h: 0, m: 0 };
                this.ligneSelectionnee = null;
                this.erreurLigne = '';
                this.modal = action;
                return;
            }
            if (def.confirm) {
                this.confirm = { ouvert: true, action, id, titre: def.label, texte: def.confirm, callback: null };
                return;
            }
            this.executer(id, action, {});
        },

        async confirmerAction() {
            const { id, action } = this.confirm;
            this.confirm.ouvert = false;
            await this.executer(id, action, {});
        },

        async executer(id, action, corps) {
            if (this.enCours) return;
            this.enCours = action;
            try {
                const r = await cmFetch(`${POSTES_URL}/${id}/${action}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(corps) });
                const d = await r.json().catch(() => ({}));
                this.afficher(d.message || (r.ok ? 'Action effectuée.' : 'Erreur.'), r.ok);
                if (r.ok) this.modal = null;
            } catch (e) { this.afficher('Serveur injoignable.', false); }
            this.enCours = null;
            await this.rafraichir();
        },

        async ajouterLigne() {
            this.erreurLigne = '';
            let m = 0;
            if (this.newLigne.type === 'montant') m = Math.round(Number(this.newLigne.montant));
            else m = (((Number(this.newLigne.h) || 0) * 60) + (Number(this.newLigne.m) || 0)) * TARIF_MINUTE;

            if (!m || m <= 0) { this.erreurLigne = 'Saisissez un montant ou une durée valide.'; return; }
            if (m < TARIF_MIN) { this.erreurLigne = `Montant trop faible : le minimum est de ${fmtAr(TARIF_MIN)}.`; return; }

            const duree = Math.round(m / TARIF_MINUTE);
            if (this.lignesForfait.some(l => Number(l.montant) === m || Number(l.duree) === duree)) {
                this.erreurLigne = 'Une recharge identique (même montant ou même durée) existe déjà dans le tableau.';
                return;
            }
            if (this.enregLigne) return;
            this.enregLigne = true;

            try {
                const r = await cmFetch(POSTES_URL + '/raccourcis', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                    body: JSON.stringify({ montant: m })
                });
                const d = await r.json().catch(() => ({}));
                if (!r.ok || !d.ok) {
                    this.erreurLigne = d.message || 'Enregistrement impossible.';
                } else {
                    this.lignesForfait.push({ id: d.id, montant: d.montant, duree: d.duree });
                    this.lignesForfait.sort((a, b) => a.montant - b.montant);
                    this.newLigne = { type: this.newLigne.type, montant: '', h: 0, m: 0 };
                }
            } catch (e) { this.erreurLigne = 'Serveur injoignable.'; }
            this.enregLigne = false;
        },

        demanderSupprimerLigne(index) {
            const ligne = this.lignesForfait[index];
            this.confirm = {
                ouvert: true,
                action: 'supprimer_ligne',
                id: null,
                titre: 'Supprimer',
                texte: 'Supprimer cette recharge du tableau ?',
                callback: async () => {
                    this.confirm.ouvert = false;
                    if (!ligne) return;
                    if (ligne.id) {
                        try {
                            const r = await cmFetch(`${POSTES_URL}/raccourcis/${ligne.id}/supprimer`, {
                                method: 'POST',
                                headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                                body: '{}'
                            });
                            const d = await r.json().catch(() => ({}));
                            if (!r.ok || !d.ok) { this.afficher(d.message || 'Suppression impossible.', false); return; }
                        } catch (e) { this.afficher('Serveur injoignable.', false); return; }
                    }
                    this.lignesForfait = this.lignesForfait.filter(l => Number(l.montant) !== Number(ligne.montant));
                }
            };
        },

        selectionnerLigne(l) {
            this.ligneSelectionnee = l.montant;
            this.form.montantManuel = '';
            this.form.h = 0; this.form.m = 0;
        },

        calculerDepuisMontant() {
            this.ligneSelectionnee = null;
            const m = Number(this.form.montantManuel);
            if (m > 0) {
                const totalMin = m / TARIF_MINUTE;
                this.form.h = Math.floor(totalMin / 60);
                this.form.m = Math.round(totalMin % 60);
            }
        },

        calculerDepuisDuree() {
            this.ligneSelectionnee = null;
            const d = (Number(this.form.h) * 60) + Number(this.form.m);
            if (d > 0) this.form.montantManuel = d * TARIF_MINUTE;
        },

        validerModal() {
            const id = this.selId;
            let montant = 0;
            if (this.ligneSelectionnee) montant = this.ligneSelectionnee;
            else if (this.form.montantManuel) montant = Number(this.form.montantManuel);
            else if (this.form.h || this.form.m) montant = ((Number(this.form.h) * 60) + Number(this.form.m)) * TARIF_MINUTE;

            if (montant <= 0) { this.afficher("Veuillez sélectionner ou saisir un montant.", false); return; }
            const duree = Math.round(montant / TARIF_MINUTE);

            if (this.modal === 'demarrer') {
                const dMin = this.dureeMinimale();
                if (montant < TARIF_MIN || duree < dMin) {
                    this.afficher(`Première activation : minimum ${fmtAr(TARIF_MIN)} (${dMin} min).`, false, 4500);
                    return;
                }
            }

            if (this.modal === 'demarrer') this.executer(id, 'demarrer', { type: 'forfait', duree_minutes: duree });
            else this.executer(id, 'recharger', { minutes: duree });
        },

        async chargerChat(reset) {
            const id = this.selId;
            if (!id || this.sel?.etat === 'hors_ligne') return;
            try {
                const r = await cmFetch(`${POSTES_URL}/${id}/chat?apres=${reset ? 0 : this.chatDernier}`);
                const d = await r.json();
                if (id !== this.selId) return;
                if (reset) this.chat = [];
                if (d.messages?.length) {
                    const existants = new Set(this.chat.map(m => m.id));
                    d.messages.forEach(m => { if (!existants.has(m.id)) { this.chat.push(m); existants.add(m.id); } });
                    this.chat.sort((a, b) => a.id - b.id);
                    this.chatDernier = d.messages[d.messages.length - 1].id;
                    this.$nextTick(() => { const b = this.$refs.chatBox; if (b) b.scrollTop = b.scrollHeight; });
                }
            } catch (e) {}
        },

        async envoyerMessage() {
            if (this.sel?.etat === 'hors_ligne') {
                this.afficher("Impossible : le poste est non présent.", false);
                return;
            }
            const t = this.texte.trim();
            if (!t || !this.selId || this.envoiChat) return;
            const id = this.selId;
            this.envoiChat = true;
            try {
                const r = await cmFetch(`${POSTES_URL}/${id}/message`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ message: t }) });
                const d = await r.json().catch(() => ({}));
                if (r.ok && d.ok) {
                    this.texte = '';
                    if (id === this.selId && d.id && !this.chat.some(m => m.id === d.id)) {
                        this.chat.push({ id: d.id, sens: 'admin', contenu: t, heure: d.heure || new Date().toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' }) });
                        this.chat.sort((a, b) => a.id - b.id);
                    }
                    this.$nextTick(() => { const b = this.$refs.chatBox; if (b) b.scrollTop = b.scrollHeight; });
                } else {
                    this.afficher(d.message || 'Message non envoyé.', false);
                }
            } catch (e) { this.afficher('Serveur injoignable.', false); }
            this.envoiChat = false;
        },

        afficher(msg, ok, ms = 3000) { this.notif = { msg, ok }; setTimeout(() => this.notif = null, ms); },
    };
}
</script>

<div x-data="postesApp()" x-init="init()" class="w-full space-y-4">
    {{-- En-tête de la page --}}
    <div class="flex items-center justify-between">
        <div>
            <h1 class="text-2xl font-semibold tracking-tight text-slate-900 dark:text-white">Postes clients</h1>
            <p class="text-sm text-slate-500 dark:text-slate-400" x-text="bilan"></p>
        </div>
    </div>

    {{-- Conteneur principal Postes --}}
    <div class="cm-postes-container">
        {{-- Colonne de gauche : Liste des postes --}}
        <aside class="cm-postes-aside flex flex-col overflow-hidden rounded-2xl border border-indigo-950 bg-indigo-950 shadow-sm">
            <div class="border-b border-white/10 px-4 py-3 text-xs font-semibold uppercase tracking-wider text-indigo-300">
                Postes de travail
            </div>
            <div class="max-h-[calc(100vh-14rem)] space-y-1.5 overflow-y-auto p-2">
                <template x-for="p in postes" :key="p.id">
                    <button type="button" @click="choisir(p.id)" @contextmenu.prevent.stop="ouvrirMenu($event, p.id)"
                            :class="selId === p.id ? 'bg-white text-slate-900 shadow' : 'bg-white/5 text-indigo-100 hover:bg-white/10'"
                            class="flex w-full items-center gap-3 rounded-xl px-3 py-2.5 text-left transition">
                        <span x-html="icone(p.etat)" class="shrink-0"></span>
                        <span class="min-w-0 flex-1">
                            <span class="block truncate text-sm font-semibold" x-text="p.nom"></span>
                            <span class="block truncate text-xs" :class="selId === p.id ? 'text-slate-500' : 'text-indigo-300'"
                                  x-text="p.session?.pause?.type?.startsWith('coupure') ? '⚡ Conservée (coupure)' : (p.session?.pause?.type === 'manuelle' ? '⏸ Pause manuelle' : libelle(p.etat))"></span>
                        </span>
                        <span x-show="p.session" x-cloak class="shrink-0 font-mono text-xs font-bold" :class="selId === p.id ? 'text-slate-700' : 'text-amber-300'" x-text="tempsListe(p)"></span>
                    </button>
                </template>
            </div>
        </aside>

        {{-- Colonne de droite : Détails du poste sélectionné --}}
        <section class="cm-postes-main flex flex-col rounded-2xl border border-slate-200 bg-white shadow-sm dark:border-slate-800 dark:bg-slate-900">
            <template x-if="!sel">
                <div class="flex flex-1 flex-col items-center justify-center p-14 text-center text-slate-400">
                    <div class="mb-3 opacity-40" x-html="icone('disponible', 'h-16 w-16')"></div>
                    Sélectionnez un poste dans la liste pour voir ses détails.
                </div>
            </template>

            <template x-if="sel">
                <div class="flex flex-col">
                    {{-- 1. Titre du poste & statut --}}
                    <div class="flex items-center gap-3 border-b border-slate-100 bg-slate-50 px-6 py-4 rounded-t-2xl dark:border-slate-800 dark:bg-slate-800/50">
                        <span x-html="icone(sel.etat, 'h-12 w-12')"></span>
                        <div class="min-w-0 flex-1">
                            <h2 class="truncate text-xl font-bold text-slate-900 dark:text-white" x-text="sel.nom"></h2>
                            <p class="text-xs text-slate-500 dark:text-slate-400" x-text="sel.ip ? 'IP : ' + sel.ip : 'IP non attribuée'"></p>
                        </div>
                        <span class="rounded-full px-3 py-1 text-xs font-bold text-white shadow-sm" :style="`background:${({disponible:'#16a34a',occupe:'#d97706',suspendu:'#7c3aed',verrouille:'#dc2626',desactive:'#64748b',hors_ligne:'#64748b'})[sel.etat]}`" x-text="libelle(sel.etat)"></span>
                    </div>

                    {{-- 2. Barre d'actions immédiatement accessible en haut --}}
                    <div class="flex flex-wrap items-center gap-2 border-b border-slate-200 bg-slate-100/70 px-6 py-3 dark:border-slate-800 dark:bg-slate-800/30">
                        <template x-for="a in sel.actions" :key="a.cle">
                            <button type="button" @click="demanderConfirm(sel.id, a.cle)" :disabled="enCours !== null" :class="classeBouton(a.couleur)" class="rounded-lg px-4 py-2 text-sm font-semibold shadow-sm transition disabled:opacity-50 flex items-center gap-2 cursor-pointer">
                                <span x-show="enCours === a.cle" class="animate-spin h-3.5 w-3.5 border-2 border-white border-t-transparent rounded-full"></span>
                                <span x-text="a.label"></span>
                            </button>
                        </template>

                        {{-- Message informatif si aucune action n'est disponible (ex: poste hors ligne sans session) --}}
                        <div x-show="sel.actions.length === 0" class="text-xs text-slate-500 italic py-1 dark:text-slate-400">
                            <span x-show="sel.etat === 'hors_ligne'">Poste éteint ou injoignable sur le réseau local. Aucune action requise.</span>
                            <span x-show="sel.etat !== 'hors_ligne'">Aucune action disponible pour cet état.</span>
                        </div>
                    </div>

                    {{-- 3. Zone de contenu principale (2 colonnes sans écrasement) --}}
                    <div class="cm-details-grid">
                        {{-- Colonne Infos Session --}}
                        <div class="flex flex-col">
                            <h3 class="mb-3 text-sm font-semibold text-slate-700 dark:text-slate-200 uppercase tracking-wider text-xs">Informations de session</h3>

                            <div class="cm-info-row">
                                <span class="cm-info-label">État du poste</span>
                                <span class="cm-info-val font-bold" x-text="libelle(sel.etat)"></span>
                            </div>

                            <template x-if="sel.session">
                                <div>
                                    <div class="cm-info-row">
                                        <span class="cm-info-label">Type</span>
                                        <span class="cm-info-val" x-text="mode(sel.session.mode)"></span>
                                    </div>
                                    <div class="cm-info-row" x-show="sel.session.mode === 'forfait'">
                                        <span class="cm-info-label">Forfait</span>
                                        <span class="cm-info-val" x-text="fmtAr(sel.session.forfait_total)"></span>
                                    </div>
                                    <div class="cm-info-row">
                                        <span class="cm-info-label">Début</span>
                                        <span class="cm-info-val" x-text="debutAffiche(sel)"></span>
                                    </div>
                                    <div class="cm-info-row" x-show="sel.session.mode === 'forfait'">
                                        <span class="cm-info-label">Temps restant</span>
                                        <span class="cm-info-val font-bold text-amber-600 dark:text-amber-400" x-text="restant(sel)"></span>
                                    </div>
                                    <div class="cm-info-row" x-show="sel.session.mode !== 'forfait'">
                                        <span class="cm-info-label">Temps écoulé</span>
                                        <span class="cm-info-val" x-text="ecoule(sel)"></span>
                                    </div>
                                    <div class="cm-info-row" x-show="sel.session.mode === 'forfait'">
                                        <span class="cm-info-label" x-text="sel.session.etat === 'terminee' ? 'Montant payé' : 'Montant forfait'"></span>
                                        <span class="cm-info-val font-bold text-emerald-600 dark:text-emerald-400" x-text="fmtAr(sel.session.forfait_total)"></span>
                                    </div>
                                    <div class="cm-info-row" x-show="sel.session.mode !== 'forfait'">
                                        <span class="cm-info-label" x-text="sel.session.etat === 'terminee' ? 'Montant final' : 'Montant actuel'"></span>
                                        <span class="cm-info-val font-bold text-emerald-600 dark:text-emerald-400" x-text="montantActuel(sel)"></span>
                                    </div>
                                    <div class="cm-info-row" x-show="sel.session.pause?.libelle">
                                        <span class="cm-info-label">Motif pause</span>
                                        <span class="cm-info-val font-semibold" :class="sel.session.pause?.type === 'manuelle' ? 'text-violet-600' : 'text-amber-600'" x-text="sel.session.pause?.libelle"></span>
                                    </div>
                                    <div class="cm-info-row" x-show="sel.session.pause?.non_facture_s > 0">
                                        <span class="cm-info-label">Temps non facturé</span>
                                        <span class="cm-info-val font-semibold text-emerald-600" x-text="fmtDuree(sel.session.pause?.non_facture_s) + ' (coupure)'"></span>
                                    </div>
                                    <div class="cm-info-row">
                                        <span class="cm-info-label">Internet</span>
                                        <span class="cm-info-val" x-text="internet(sel.internet)"></span>
                                    </div>
                                    <div class="mt-2 text-right" x-show="sel.session.pause?.incident_id">
                                        <a :href="'{{ url('/incidents') }}/' + sel.session.pause?.incident_id" class="text-xs font-semibold text-indigo-600 hover:underline">Voir l'incident associé →</a>
                                    </div>
                                </div>
                            </template>

                            <template x-if="!sel.session">
                                <div class="mt-4 rounded-xl border border-dashed border-slate-200 bg-slate-50 p-6 text-center text-sm text-slate-400 dark:border-slate-800 dark:bg-slate-800/40">
                                    Aucune session active sur ce poste.
                                </div>
                            </template>
                        </div>

                        {{-- Colonne Discussion Client --}}
                        <div class="flex flex-col">
                            <h3 class="mb-3 text-sm font-semibold text-slate-700 dark:text-slate-200 uppercase tracking-wider text-xs">Discussion avec le client</h3>

                            <div class="flex flex-1 flex-col rounded-xl border border-slate-200 bg-slate-50 p-3 min-h-[300px] max-h-[360px] dark:border-slate-800 dark:bg-slate-800/40">
                                <div x-ref="chatBox" class="flex-1 space-y-2 overflow-y-auto pr-1">
                                    <template x-for="m in chat" :key="m.id">
                                        <div :class="m.sens === 'admin' ? 'text-right' : 'text-left'">
                                            <span class="inline-block max-w-[85%] rounded-2xl px-3 py-1.5 text-sm" :class="m.sens === 'admin' ? 'bg-indigo-600 text-white' : 'bg-white text-slate-800 ring-1 ring-slate-200 dark:bg-slate-700 dark:text-white dark:ring-slate-600'" x-text="m.contenu"></span>
                                            <div class="text-[10px] text-slate-400 mt-0.5" x-text="(m.sens === 'admin' ? 'Moi' : 'Client') + ' · ' + m.heure"></div>
                                        </div>
                                    </template>
                                    <div x-show="chat.length === 0" x-cloak class="flex h-full items-center justify-center py-10 text-center text-xs text-slate-400">
                                        Aucun message.
                                    </div>
                                </div>

                                {{-- État 1 : Poste hors ligne (injoignable) --}}
                                <div x-show="sel.etat === 'hors_ligne'" x-cloak class="mt-2 rounded-lg bg-slate-200/60 p-2.5 text-center text-xs text-slate-600 dark:bg-slate-800 dark:text-slate-400 border border-slate-300 dark:border-slate-700">
                                    Poste non présent — discussion indisponible.
                                </div>

                                {{-- État 2 : Session fermée / Poste désactivé --}}
                                <div x-show="sel.etat === 'desactive'" x-cloak class="mt-2 text-center text-xs text-slate-400 border-t pt-2 dark:border-slate-700">
                                    Session terminée — conversation en lecture seule (effacée à la prochaine activation).
                                </div>

                                {{-- État 3 : Poste en ligne et actif --}}
                                <form x-show="sel.etat !== 'desactive' && sel.etat !== 'hors_ligne'" @submit.prevent="envoyerMessage()" class="mt-2 flex gap-2 border-t pt-2 dark:border-slate-700">
                                    <input x-model="texte" maxlength="500" :disabled="envoiChat" placeholder="Écrire au client..." class="flex-1 rounded-lg border-slate-300 text-sm focus:border-indigo-500 focus:ring-indigo-500 dark:border-slate-700 dark:bg-slate-800 dark:text-white">
                                    <button type="submit" :disabled="!texte.trim() || envoiChat" class="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-700 disabled:opacity-40 flex items-center gap-1 cursor-pointer">
                                        <span x-show="envoiChat" x-cloak class="animate-spin h-3 w-3 border-2 border-white border-t-transparent rounded-full"></span>
                                        Envoyer
                                    </button>
                                </form>
                            </div>
                        </div>
                    </div>
                </div>
            </template>
        </section>
    </div>

    {{-- Menu contextuel clic droit --}}
    <div x-show="menu.ouvert" x-cloak @click.stop :style="`left:${menu.x}px; top:${menu.y}px; z-index:9400`" class="fixed w-52 overflow-hidden rounded-xl border border-slate-200 bg-white py-1 shadow-2xl dark:border-slate-800 dark:bg-slate-800">
        <template x-for="a in (postes.find(p => p.id === menu.id)?.actions || [])" :key="a.cle">
            <button type="button" @click="demanderConfirm(menu.id, a.cle)" class="flex w-full items-center gap-2 px-4 py-2 text-left text-sm text-slate-700 hover:bg-indigo-50 dark:text-slate-200 dark:hover:bg-slate-700">
                <span class="h-2 w-2 rounded-full" :class="{'bg-emerald-500': a.couleur==='vert', 'bg-rose-500': a.couleur==='rouge', 'bg-amber-500': a.couleur==='ambre', 'bg-violet-500': a.couleur==='violet', 'bg-sky-500': a.couleur==='bleu', 'bg-slate-500': a.couleur==='gris' }"></span>
                <span x-text="a.label"></span>
            </button>
        </template>
    </div>

    {{-- Modale Démarrer / Recharger --}}
    <div x-show="modal === 'demarrer' || modal === 'recharger'" x-cloak class="cm-overlay" style="z-index: 9300;">
        <div class="cm-backdrop" @click="modal = null"></div>
        <div class="relative w-full max-w-3xl rounded-2xl bg-white shadow-2xl flex flex-col dark:bg-slate-900" style="max-width: 48rem;">
            <div class="border-b border-slate-100 p-4 dark:border-slate-800">
                <h2 class="text-lg font-bold text-slate-900 dark:text-white" x-text="(modal === 'demarrer' ? 'Démarrer (Forfait)' : 'Recharger (Forfait)') + (sel ? ' — ' + sel.nom : '')"></h2>
                <p class="text-xs text-slate-500 mt-1 dark:text-slate-400" x-text="'Tarif : ' + TARIF_MINUTE + ' Ar/min • Minimum : ' + TARIF_MIN + ' Ar'"></p>
            </div>
            <div class="p-4" style="display: grid; grid-template-columns: minmax(0, 1.15fr) minmax(0, 1fr); gap: 24px;">
                <div class="border-r pr-4 dark:border-slate-800" style="min-width: 0;">
                    <div class="flex justify-between items-center mb-2" style="gap: 8px;">
                        <h3 class="font-semibold text-sm text-slate-800 dark:text-slate-200">Recharges rapides</h3>
                        <button type="button" @click="newLigne.type = (newLigne.type === 'montant' ? 'duree' : 'montant'); erreurLigne = ''" class="cm-bascule">
                            ⇄ Basculer saisie (Montant/Durée)
                        </button>
                    </div>
                    <div class="mb-4 bg-slate-50 p-2 rounded border dark:bg-slate-800/50 dark:border-slate-700">
                        <div class="flex gap-2 items-end">
                            <template x-if="newLigne.type === 'montant'">
                                <div class="flex-1" style="min-width: 0;">
                                    <label class="text-[10px] text-slate-500 dark:text-slate-400">Montant (Ar)</label>
                                    <input type="number" x-model="newLigne.montant" @input="erreurLigne = ''" @keydown.enter.prevent="ajouterLigne()" class="w-full text-xs p-1 border-slate-300 rounded dark:bg-slate-700 dark:border-slate-600 dark:text-white" :style="erreurLigne ? 'border-color:#dc2626' : ''">
                                </div>
                            </template>
                            <template x-if="newLigne.type === 'duree'">
                                <div class="flex-1 flex gap-1" style="min-width: 0;">
                                    <div style="flex: 1; min-width: 0;"><label class="text-[10px] text-slate-500 dark:text-slate-400">H</label><input type="number" x-model="newLigne.h" @input="erreurLigne = ''" class="w-full text-xs p-1 border-slate-300 rounded dark:bg-slate-700 dark:border-slate-600 dark:text-white" :style="erreurLigne ? 'border-color:#dc2626' : ''"></div>
                                    <div style="flex: 1; min-width: 0;"><label class="text-[10px] text-slate-500 dark:text-slate-400">Min</label><input type="number" x-model="newLigne.m" @input="erreurLigne = ''" class="w-full text-xs p-1 border-slate-300 rounded dark:bg-slate-700 dark:border-slate-600 dark:text-white" :style="erreurLigne ? 'border-color:#dc2626' : ''"></div>
                                </div>
                            </template>
                            <button type="button" @click="ajouterLigne()" class="bg-slate-800 text-white px-3 py-1 rounded text-xs hover:bg-slate-700 h-[28px] cursor-pointer">Ajouter</button>
                        </div>
                        <p x-show="erreurLigne" x-cloak class="cm-err" x-text="erreurLigne"></p>
                    </div>

                    <div class="cm-table-wrap">
                        <table class="cm-table dark:text-slate-200">
                            <colgroup><col style="width: 27%;"><col style="width: 29%;"><col style="width: 44%;"></colgroup>
                            <thead><tr><th class="dark:bg-slate-800 dark:border-slate-700">Durée</th><th class="dark:bg-slate-800 dark:border-slate-700">Montant</th><th class="dark:bg-slate-800 dark:border-slate-700">Actions</th></tr></thead>
                            <tbody>
                                <template x-for="(r, i) in lignesForfait" :key="i">
                                    <tr @click="selectionnerLigne(r)" :class="ligneSelectionnee === r.montant ? 'bg-indigo-50 font-bold dark:bg-indigo-900/30' : 'cursor-pointer hover:bg-slate-50 dark:hover:bg-slate-800/40'">
                                        <td x-text="r.duree + ' min'"></td>
                                        <td x-text="r.montant + ' Ar'"></td>
                                        <td>
                                            <button type="button" @click.stop="demanderSupprimerLigne(i)" class="text-rose-600 hover:text-rose-800 text-xs flex items-center gap-1 cursor-pointer" style="color: #e11d48;"><svg class="w-3 h-3" style="width: 12px; height: 12px;" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"></path></svg> Supprimer</button>
                                        </td>
                                    </tr>
                                </template>
                            </tbody>
                        </table>
                    </div>
                </div>
                <div style="min-width: 0;">
                    <h3 class="font-semibold text-sm mb-2 text-slate-800 dark:text-slate-200">Saisie manuelle</h3>
                    <p x-show="modal === 'demarrer'" x-cloak class="text-[11px] text-slate-500 mb-3 dark:text-slate-400" x-text="'Minimum (1re activation) : ' + fmtAr(TARIF_MIN) + ' / ' + dureeMinimale() + ' min'"></p>
                    <div class="mb-4">
                        <label class="text-xs text-slate-500 dark:text-slate-400">Montant (Ar)</label>
                        <input type="number" x-model="form.montantManuel" @input="calculerDepuisMontant" class="w-full rounded border-slate-300 dark:bg-slate-800 dark:border-slate-700 dark:text-white" style="box-sizing: border-box;">
                    </div>
                    <div>
                        <label class="text-xs text-slate-500 dark:text-slate-400">Durée</label>
                        <div class="flex items-center gap-2">
                            <input type="number" min="0" x-model="form.h" @input="calculerDepuisDuree" class="cm-num rounded border-slate-300 dark:bg-slate-800 dark:border-slate-700 dark:text-white" placeholder="0"> <span class="text-sm text-slate-600 dark:text-slate-300">h</span>
                            <input type="number" min="0" x-model="form.m" @input="calculerDepuisDuree" class="cm-num rounded border-slate-300 dark:bg-slate-800 dark:border-slate-700 dark:text-white" placeholder="0"> <span class="text-sm text-slate-600 dark:text-slate-300">min</span>
                        </div>
                    </div>
                </div>
            </div>
            <div class="flex justify-end gap-2 rounded-b-2xl bg-slate-50 p-4 border-t dark:bg-slate-800 dark:border-slate-700">
                <button type="button" @click="modal = null" class="rounded-lg border bg-white px-4 py-2 text-sm dark:bg-slate-700 dark:border-slate-600 dark:text-white cursor-pointer">Annuler</button>
                <button type="button" @click="validerModal()" :disabled="enCours !== null" class="rounded-lg bg-emerald-600 px-5 py-2 text-sm font-semibold text-white hover:bg-emerald-700 flex items-center gap-1 cursor-pointer">
                    <span x-show="enCours === 'demarrer' || enCours === 'recharger'" class="animate-spin h-3 w-3 border-2 border-white border-t-transparent rounded-full"></span>
                    <span x-text="modal === 'demarrer' ? 'Démarrer' : 'Recharger'"></span>
                </button>
            </div>
        </div>
    </div>

    {{-- Boîte de dialogue de confirmation --}}
    <div x-show="confirm.ouvert" x-cloak class="cm-overlay" style="z-index: 9600;">
        <div class="cm-backdrop" @click="confirm.ouvert = false"></div>
        <div class="cm-confirm">
            <h3 x-text="confirm.titre"></h3>
            <p x-text="confirm.texte"></p>
            <div style="display: flex; justify-content: center; gap: 12px;">
                <button type="button" @click="confirm.ouvert = false" class="rounded-lg border bg-white px-4 py-2 text-xs font-medium hover:bg-slate-50 dark:bg-slate-700 dark:border-slate-600 dark:text-white cursor-pointer">Annuler</button>
                <button type="button" @click="confirm.callback ? confirm.callback() : confirmerAction()" class="rounded-lg bg-indigo-600 px-4 py-2 text-xs font-bold text-white hover:bg-indigo-700 flex items-center gap-1 cursor-pointer">
                    <span x-show="enCours === confirm.action" class="animate-spin h-3 w-3 border-2 border-white border-t-transparent rounded-full"></span>
                    Confirmer
                </button>
            </div>
        </div>
    </div>

    {{-- Notification Toast --}}
    <div x-show="notif" x-cloak x-transition class="fixed bottom-6 right-6 max-w-md rounded-xl px-4 py-3 text-sm font-medium text-white shadow-lg" style="z-index: 9700;" :class="notif?.ok ? 'bg-emerald-600' : 'bg-rose-600'" x-text="notif?.msg"></div>
</div>
@endsection