<?php

namespace App\Support;

use Carbon\CarbonInterface;

/** Affichage des dates au gérant (l'application stocke en UTC). */
final class Heure
{
    public static function fuseau(): string
    {
        return (string) config('cyber.fuseau_affichage', 'Indian/Antananarivo');
    }

    public static function local(?CarbonInterface $d, string $format = 'd/m/Y H:i'): string
    {
        return $d ? $d->copy()->setTimezone(self::fuseau())->format($format) : '—';
    }

    /** 3725 → « 1 h 02 min » ; 45 → « 45 s ». */
    public static function duree(?int $secondes): string
    {
        if ($secondes === null) {
            return '—';
        }
        $s = max(0, $secondes);
        if ($s < 60)   { return $s . ' s'; }
        if ($s < 3600) { return intdiv($s, 60) . ' min'; }

        return intdiv($s, 3600) . ' h ' . str_pad((string) intdiv($s % 3600, 60), 2, '0', STR_PAD_LEFT) . ' min';
    }
}