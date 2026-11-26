<?php

namespace App\Services;

use App\Models\Poste;
use Illuminate\Support\Facades\Cache;
use Illuminate\Support\Facades\Log;

/**
 * Contrôle d'accès Internet d'un poste, SANS identifiant. UNIQUE autorité réseau.
 * Principe : le serveur autorise / retire l'adresse MAC du poste sur le routeur.
 * Pilote disponible : MikroTik (HotSpot, ip-binding). Sans routeur configuré : « non contrôlé » (rien ne casse).
 */
final class AccesInternetService
{
    public const AUTORISE = 'autorise';
    public const COUPE    = 'coupe';
    public const ERREUR   = 'erreur';
    public const NON_GERE = 'non_gere';

    private const MARQUE = 'Cyber Manager - Poste';

    public function disponible(): bool
    {
        return (new MikroTikService())->estConfigure();
    }

    /** @return bool|null true = fait, false = échec routeur, null = aucun routeur (non contrôlé) */
    public function autoriser(Poste $poste): ?bool
    {
        return $this->appliquer($poste, true);
    }

    public function couper(Poste $poste): ?bool
    {
        return $this->appliquer($poste, false);
    }

    /** Règle unique : Internet seulement si la session tourne ET que le poste donne signe de vie. */
    public function souhaite(Poste $poste): bool
    {
        $s = $poste->sessionActive();

        return $s !== null && $s->etat === 'en_cours' && $poste->estEnLigne();
    }

    /**
     * Remet les routeurs d'aplomb : appelée par le planificateur (postes:maintenance --reseau, toutes les 30 s).
     * Une session figée (en_pause) ou un poste qui disparaît perd Internet.
     *
     * @return array{connectes:int, coupes:int, erreurs:int}
     */
    public function reconcilier(): array
    {
        $bilan = ['connectes' => 0, 'coupes' => 0, 'erreurs' => 0];

        if (Cache::has('acces.pause')) {
            return $bilan;
        }

        try {
            if (!$this->disponible()) {
                return $bilan;
            }

            $postes = Poste::whereNotNull('adresse_mac')->where('adresse_mac', '!=', '')->get();
            foreach ($postes as $p) {
                $voulu = $this->souhaite($p);
                if ($p->internet_etat === ($voulu ? self::AUTORISE : self::COUPE)) {
                    continue;
                }

                $res = $this->appliquer($p, $voulu);
                if ($res === true) {
                    $voulu ? $bilan['connectes']++ : $bilan['coupes']++;
                } elseif ($res === false) {
                    $bilan['erreurs']++;
                    Cache::put('acces.pause', 1, 30);   // routeur en panne : on ne s'acharne pas
                    break;
                }
            }
        } catch (\Throwable $e) {
            Log::warning('Accès Internet : réconciliation échouée (' . $e->getMessage() . ').');
        }

        return $bilan;
    }

    /**
     * Version limitée (au plus une fois / 10 s) pour les pages web (liste des postes).
     * Ne doit PLUS être appelée par les heartbeats des agents.
     */
    public function reconcilierTous(): void
    {
        if (!Cache::add('acces.reconcilier', 1, 10)) {
            return;
        }

        $this->reconcilier();
    }

    private function appliquer(Poste $poste, bool $autoriser): ?bool
    {
        $mt = new MikroTikService(['timeout' => 3.0]);

        if (!$mt->estConfigure()) {
            $this->noter($poste, self::NON_GERE);
            return null;
        }

        $mac = trim((string) $poste->adresse_mac);
        if ($mac === '') {
            $this->noter($poste, self::ERREUR);
            return false;
        }

        try {
            if ($autoriser) {
                $mt->autoriserMac($mac, self::MARQUE . ' : ' . $poste->nom_poste);
            } else {
                $mt->revoquerMac($mac);
            }
        } catch (\Throwable $e) {
            Log::warning("Internet du poste « {$poste->nom_poste} » : commande routeur échouée ({$e->getMessage()}).");
            $this->noter($poste, self::ERREUR);
            return false;
        }

        $this->noter($poste, $autoriser ? self::AUTORISE : self::COUPE);
        return true;
    }

    private function noter(Poste $poste, string $etat): void
    {
        Poste::whereKey($poste->id)->update(['internet_etat' => $etat, 'internet_maj_at' => now()]);
        $poste->internet_etat = $etat;
    }
}