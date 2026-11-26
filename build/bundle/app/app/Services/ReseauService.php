<?php

namespace App\Services;

/**
 * Service Réseau dynamique — Conforme Règle 1 & 3 (Aucune IP en dur, tolérance multi-cartes).
 */
class ReseauService
{
    /** Adresse de la carte réseau qui sert à joindre le réseau (route par défaut). */
    public static function ipPrincipale(): ?string
    {
        try {
            $s = @stream_socket_client('udp://192.0.2.1:53', $errno, $errstr, 1);
            if ($s) {
                $nom = (string) @stream_socket_get_name($s, false);
                @fclose($s);
                $ip = explode(':', $nom)[0] ?? '';
                if (self::valide($ip)) {
                    return $ip;
                }
            }
        } catch (\Throwable) {
        }

        return self::toutes()[0] ?? null;
    }

    /**
     * Détecte dynamiquement l'adresse IP de la passerelle par défaut (le routeur MikroTik ou la box).
     * Zéro adresse en dur : utilise la table de routage active de la machine.
     */
    public static function passerelleParDefaut(): ?string
    {
        // 1. Sous Windows (route print / netsh / wmic / PowerShell)
        if (strtoupper(substr(PHP_OS, 0, 3)) === 'WIN') {
            try {
                $output = @shell_exec('route print 0.0.0.0');
                if ($output && preg_match('/0\.0\.0\.0\s+0\.0\.0\.0\s+([0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3})/', $output, $matches)) {
                    $gw = trim($matches[1]);
                    if (self::valide($gw)) {
                        return $gw;
                    }
                }
            } catch (\Throwable) {}

            try {
                $output = @shell_exec('powershell -NoProfile -Command "(Get-NetRoute -DestinationPrefix \'0.0.0.0/0\' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty NextHop)[0]"');
                $gw = trim((string) $output);
                if (self::valide($gw)) {
                    return $gw;
                }
            } catch (\Throwable) {}
        } else {
            // 2. Sous Linux / Mac
            try {
                $output = @shell_exec("ip route show default 2>/dev/null | awk '{print $3}'");
                $gw = trim((string) $output);
                if (self::valide($gw)) {
                    return $gw;
                }
            } catch (\Throwable) {}
        }

        return null;
    }

    /**
     * Teste si une adresse IP et un port API MikroTik répondent sur le réseau local.
     */
    public static function testerPort(string $ip, int $port = 8728, float $timeout = 1.5): bool
    {
        if (!self::valide($ip)) {
            return false;
        }

        $fp = @stream_socket_client("tcp://{$ip}:{$port}", $errno, $errstr, $timeout);
        if ($fp) {
            @fclose($fp);
            return true;
        }
        return false;
    }

    /** Toutes les adresses IPv4 valides de l'ordinateur (tolérance multi-cartes). */
    public static function toutes(): array
    {
        $liste = [];

        try {
            if (function_exists('net_get_interfaces')) {
                foreach ((array) @net_get_interfaces() as $nom => $carte) {
                    if (isset($carte['up']) && $carte['up'] === false) {
                        continue;
                    }
                    if (preg_match('/vEthernet|VirtualBox|VMware|Hyper-V|WSL|Loopback|Bluetooth|Tailscale|ZeroTier/i', (string) $nom)) {
                        continue;
                    }
                    foreach ((array) ($carte['unicast'] ?? []) as $adresse) {
                        $ip = (string) ($adresse['address'] ?? '');
                        if (self::valide($ip)) {
                            $liste[] = $ip;
                        }
                    }
                }
            }
        } catch (\Throwable) {
        }

        try {
            foreach ((array) @gethostbynamel((string) gethostname()) as $ip) {
                if (self::valide($ip)) {
                    $liste[] = $ip;
                }
            }
        } catch (\Throwable) {
        }

        return array_values(array_unique($liste));
    }

    public static function valide(string $ip): bool
    {
        return filter_var($ip, FILTER_VALIDATE_IP, FILTER_FLAG_IPV4)
            && !str_starts_with($ip, '127.')
            && !str_starts_with($ip, '169.254.')
            && $ip !== '0.0.0.0';
    }

    public static function urlServeur(?int $port = null): string
    {
        $requete = function_exists('request') ? request() : null;

        $port = ($port !== null && $port > 0) ? $port : (int) ($requete?->getPort() ?: 80);
        $hote = (string) ($requete?->getHost() ?? '');

        $ip = null;
        if ($hote !== '' && filter_var($hote, FILTER_VALIDATE_IP, FILTER_FLAG_IPV4) && self::valide($hote)
            && in_array($hote, self::toutes(), true)) {
            $ip = $hote;
        }
        $ip ??= self::ipPrincipale() ?? '127.0.0.1';

        return 'http://' . $ip . ($port === 80 ? '' : ':' . $port);
    }
}