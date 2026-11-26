using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CyberManager.Server.Setup;

/// <summary>
/// Règle du cahier (§2.5.2) : une seule carte → on la prend ; plusieurs → on présélectionne celle qui porte
/// la passerelle par défaut. « Privé » est une information, PAS un filtre : un réseau de cyber peut très bien
/// utiliser une plage non RFC1918 (le labo utilise du 128.x).
/// </summary>
internal static class DetectionReseau
{
    private static readonly string[] MotsVirtuels =
    {
        "virtual", "vmware", "hyper-v", "vethernet", "vbox", "tap-", "tap windows", "wintun",
        "loopback", "bluetooth", "vpn", "wsl", "npcap", "docker", "hamachi", "zerotier", "tailscale"
    };

    public static InfoReseau Detecter()
    {
        var info = new InfoReseau();

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

            IPInterfaceProperties props;
            try { props = ni.GetIPProperties(); }
            catch { continue; }

            var adresses = props.UnicastAddresses
                .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(u => new AdresseIp { Ip = u.Address.ToString(), Prefixe = u.PrefixLength })
                .ToList();
            if (adresses.Count == 0) continue;

            var passerelle = props.GatewayAddresses
                .Select(g => g.Address)
                .FirstOrDefault(g => g.AddressFamily == AddressFamily.InterNetwork && !g.Equals(IPAddress.Any));

            var dhcp = false;
            try { dhcp = props.GetIPv4Properties().IsDhcpEnabled; }
            catch { /* inconnu */ }

            var texte = (ni.Name + " " + ni.Description).ToLowerInvariant();
            var virtuelle = MotsVirtuels.Any(m => texte.Contains(m));
            var apipa = adresses.All(a => a.Ip.StartsWith("169.254.", StringComparison.Ordinal));

            info.Interfaces.Add(new InterfaceReseau
            {
                Nom = ni.Name,
                Description = ni.Description,
                Type = TypeDe(ni.NetworkInterfaceType),
                Virtuelle = virtuelle,
                Eligible = !virtuelle && !apipa,
                Prive = adresses.Any(a => EstPrive(a.Ip)),
                Dhcp = dhcp,
                Passerelle = passerelle?.ToString(),
                Adresses = adresses
            });
        }

        var eligibles = info.Interfaces.Where(i => i.Eligible).ToList();
        info.NombreEligibles = eligibles.Count;

        var choix = eligibles
            .OrderBy(i => i.Passerelle == null ? 1 : 0)
            .ThenBy(i => i.Type == "ethernet" ? 0 : i.Type == "wifi" ? 2 : 1)
            .ThenBy(i => i.Nom, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        foreach (var i in info.Interfaces)
        {
            i.Raison = i.Virtuelle ? "Carte virtuelle (VirtualBox, VMware, VPN…) : ignorée."
                : !i.Eligible ? "Adresse automatique non attribuée (169.254.x.x) : réseau non connecté."
                : i.Passerelle != null ? "Connectée au réseau et à la passerelle (routeur)."
                : "Connectée, sans passerelle (réseau local fermé).";
        }

        if (choix != null)
        {
            choix.Recommandee = true;
            info.InterfaceRecommandee = choix.Nom;
            info.AdresseRecommandee = choix.Adresses[0].Ip;
        }

        return info;
    }

    private static string TypeDe(NetworkInterfaceType t) => t switch
    {
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit => "ethernet",
        NetworkInterfaceType.Wireless80211 => "wifi",
        _ => "autre"
    };

    internal static bool EstPrive(string ip)
    {
        if (!IPAddress.TryParse(ip, out var a) || a.AddressFamily != AddressFamily.InterNetwork) return false;

        var b = a.GetAddressBytes();
        return b[0] == 10
               || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
               || (b[0] == 192 && b[1] == 168);
    }
}