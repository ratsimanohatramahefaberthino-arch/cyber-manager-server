<div x-show="tab === 'reseau'" x-cloak class="space-y-6" role="tabpanel">

    {{-- CARTE 1 : ACCÈS ROUTEUR MIKROTIK --}}
    <x-portail.carte titre="Liaison avec le routeur MikroTik" aide="Permet à Cyber Manager de piloter le Wi-Fi, couper les connexions expirées et rediriger les clients." icone="key">
        <div class="space-y-4">

            {{-- Bloc d'assistance intelligente Zéro Terminal --}}
            <div class="rounded-xl border border-violet-200 bg-violet-50/80 p-4 text-xs text-slate-800 space-y-3 dark:border-violet-900/50 dark:bg-violet-950/30 dark:text-violet-200">
                <div class="flex items-center justify-between">
                    <p class="font-bold text-violet-950 dark:text-violet-100 text-sm flex items-center gap-1.5">
                        <x-icone nom="wifi" class="h-4 w-4 text-violet-600" />
                        Détection automatique de votre boîtier MikroTik
                    </p>
                    <button type="button" @click="scannerRouteur()" :disabled="detectionEnCours"
                            class="inline-flex items-center gap-1.5 rounded-lg bg-violet-600 px-3 py-1.5 font-bold text-white shadow hover:bg-violet-700 disabled:opacity-50">
                        <span x-bind:class="detectionEnCours ? 'animate-spin inline-block' : 'inline-block'">
                            <x-icone nom="refresh" class="h-3.5 w-3.5" />
                        </span>
                        <span x-text="detectionEnCours ? 'Scan en cours...' : '🔍 Détecter et tester mon routeur'"></span>
                    </button>
                </div>

                <p class="leading-relaxed">
                    Cliquez sur le bouton ci-dessus : Cyber Manager analyse votre câble ou réseau Wi-Fi, trouve l'adresse de votre boîtier et teste son port d'échange (8728).
                </p>

                <div x-show="detectionMessage" x-cloak
                     :class="{
                        'border-emerald-300 bg-emerald-50 text-emerald-800': detectionStatut === 'succes',
                        'border-amber-300 bg-amber-50 text-amber-800': detectionStatut === 'avertissement',
                        'border-rose-300 bg-rose-50 text-rose-800': detectionStatut === 'erreur',
                        'border-violet-200 bg-white text-slate-700': detectionStatut === 'info'
                     }"
                     class="rounded-lg border p-2.5 font-medium transition">
                    <span x-text="detectionMessage"></span>
                </div>

                {{-- Explications simples des identifiants demandés --}}
                <div class="pt-2 border-t border-violet-200/60 dark:border-violet-900/40 text-[11px] space-y-1 text-slate-600 dark:text-slate-300">
                    <p><strong>Identifiant & Mot de passe :</strong> Ce sont les accès de connexion au logiciel <em>Winbox</em> de votre boîtier MikroTik (d'usine : utilisateur <code>admin</code>, mot de passe vide ou noté sur l'étiquette sous le boîtier).</p>
                    <p>💡 <em>Si vous gérez exclusivement des ordinateurs fixes (PC) et aucun Wi-Fi, vous pouvez laisser ces champs vides.</em></p>
                </div>
            </div>

            <label class="flex items-center gap-2 text-xs text-slate-600 dark:text-slate-400">
                <input type="checkbox" name="efface_routeur" value="1" class="rounded border-slate-300 text-violet-600 focus:ring-violet-500">
                <span>Désactiver le routeur (passer en mode réseau local autonome sans Wi-Fi).</span>
            </label>

            {{-- Champs de formulaire --}}
            <div class="grid grid-cols-1 gap-3 sm:grid-cols-3">
                <div class="sm:col-span-2">
                    <label for="mt-host" class="{{ $label }}">Adresse IP du boîtier MikroTik</label>
                    <input id="mt-host" type="text" name="mikrotik_host"
                           value="{{ old('mikrotik_host', $routeurConfig['host']) }}"
                           placeholder="Ex : 192.168.88.1" inputmode="decimal" autocomplete="off" class="{{ $champ }} font-mono">
                    <p class="mt-1 text-[11px] text-slate-500">Détecté automatiquement via le bouton ci-dessus ou votre passerelle réseau.</p>
                </div>
                <div>
                    <label for="mt-port" class="{{ $label }}">Port API RouterOS</label>
                    <input id="mt-port" type="number" name="mikrotik_port"
                           value="{{ old('mikrotik_port', $routeurConfig['port'] ?: 8728) }}"
                           min="1" max="65535" class="{{ $champ }} font-mono">
                    <p class="mt-1 text-[11px] text-slate-500">Standard d'usine : 8728</p>
                </div>
            </div>

            <div class="grid grid-cols-1 gap-3 sm:grid-cols-2">
                <div>
                    <label for="mt-user" class="{{ $label }}">Nom d'utilisateur Winbox</label>
                    <input id="mt-user" type="text" name="mikrotik_user"
                           value="{{ old('mikrotik_user', $routeurConfig['user'] ?: 'admin') }}" autocomplete="off" class="{{ $champ }}">
                    <p class="mt-1 text-[11px] text-slate-500">Par défaut sur un matériel neuf : admin</p>
                </div>
                <div>
                    <label for="mt-pass" class="{{ $label }}">Mot de passe Winbox</label>
                    <input id="mt-pass" type="password" name="mikrotik_password" autocomplete="new-password"
                           placeholder="{{ $routeurConfig['mot_de_passe_defini'] ? 'Mot de passe configuré (laisser vide pour conserver)' : 'Laisser vide si aucun mot de passe' }}"
                           class="{{ $champ }}">
                    <p class="mt-1 text-[11px] text-slate-500">Le mot de passe de votre routeur (neuf : vide)</p>
                </div>
            </div>
        </div>
    </x-portail.carte>

    {{-- CARTE 2 : REDIRECTION DU SERVEUR SUR LE RÉSEAU --}}
    <x-portail.carte titre="Adresse du serveur pour les téléphones" aide="Adresse utilisée par les clients pour charger la page de connexion avant d'accéder à Internet." icone="globe">
        <div class="space-y-4">
            <div>
                <label for="f-domaine" class="{{ $label }}">Nom de domaine simplifié (facultatif)</label>
                <div class="flex">
                    <span class="inline-flex items-center rounded-l-lg border border-r-0 border-slate-300 bg-slate-50 px-3 text-sm text-slate-500">http://</span>
                    <input id="f-domaine" type="text" name="portail_domaine" x-model="form.portail_domaine" maxlength="100"
                           placeholder="portail.cyber"
                           autocomplete="off" autocapitalize="off" spellcheck="false" class="{{ $champ }} rounded-l-none font-mono">
                </div>
                <p class="mt-1 text-xs text-slate-500">Nom mémorisable écrit automatiquement dans le DNS du routeur (ex : portail.cyber).</p>
            </div>

            <div>
                <span class="{{ $label }}">Adresse IP de cet ordinateur (Serveur)</span>
                <input type="text" readonly value="{{ $ipServeur ?? 'Non détectée' }}"
                       class="w-full rounded-lg border-slate-200 bg-slate-50 font-mono text-sm {{ $ipServeur ? 'text-slate-800' : 'text-rose-600' }}">
                <details class="mt-2 text-xs text-slate-600 dark:text-slate-400" @if ($errors->has('portail_ip_serveur') || $ipManuelle) open @endif>
                    <summary class="cursor-pointer font-semibold text-violet-700 hover:underline">Définir une adresse IP manuellement si plusieurs cartes réseau sont actives</summary>
                    <div class="mt-2">
                        <input type="text" name="portail_ip_serveur" value="{{ old('portail_ip_serveur', $ipManuelle) }}" inputmode="decimal"
                               placeholder="Laisser vide pour la détection automatique" autocomplete="off" class="{{ $champ }} font-mono">
                        <p class="mt-1">Renseignez ce champ uniquement si vous possédez plusieurs cartes réseau et souhaitez forcer l'IP visible des clients.</p>
                    </div>
                </details>
            </div>

            @if ($urlIp)
                <div>
                    <span class="{{ $label }}">Lien direct de redirection</span>
                    <div class="flex gap-2">
                        <input type="text" readonly value="{{ $urlIp }}" class="w-full rounded-lg border-slate-200 bg-slate-50 font-mono text-xs text-slate-700">
                        <button type="button" @click="cmCopy(@js($urlIp))" class="{{ $bouton2 }} shrink-0 !px-3">
                            <x-icone nom="copy" class="h-4 w-4" /> Copier
                        </button>
                    </div>
                </div>
            @endif
        </div>
    </x-portail.carte>

    {{-- CARTE 3 : DÉPANNAGE ET RÉINITIALISATION ROUTEUR --}}
    <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm sm:p-6 dark:border-slate-800 dark:bg-slate-900">
        <h2 class="text-base font-bold text-slate-900 dark:text-white">Le portail ne s'affiche pas sur certains téléphones ?</h2>
        <p class="mt-1 text-xs text-slate-600 dark:text-slate-400 leading-relaxed">
            Ce bouton recalcule l'adresse IP du PC, nettoie les règles obsolètes du routeur, autorise l'accès préalable (Walled Garden)
            et redémarre le service HotSpot du boîtier.
        </p>
        <button type="submit" formaction="{{ route('portail.admin.configurer') }}" :disabled="envoi"
                @click="if (! confirm('Synchroniser et redémarrer le HotSpot MikroTik ? Les clients connectés seront temporairement rafraîchis.')) $event.preventDefault()"
                class="mt-3 inline-flex items-center justify-center gap-2 rounded-xl bg-slate-900 px-4 py-2.5 text-xs font-semibold text-white shadow-sm transition hover:bg-slate-800 focus:outline-none focus-visible:ring-2 focus-visible:ring-slate-900 disabled:cursor-wait disabled:opacity-60">
            <x-icone nom="refresh" class="h-4 w-4" /> Réinitialiser et redémarrer le HotSpot
        </button>
    </section>
</div>