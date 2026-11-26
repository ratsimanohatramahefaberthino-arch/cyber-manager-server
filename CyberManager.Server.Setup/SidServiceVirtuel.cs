using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace CyberManager.Server.Setup;

/// <summary>
/// SID d'un compte de service virtuel (NT SERVICE\nom) : S-1-5-80 puis 5 blocs de 32 bits tirés du SHA-1 du nom
/// en majuscules (UTF-16). On peut donc accorder des droits NTFS AVANT que le service existe.
/// </summary>
internal static class SidServiceVirtuel
{
    public static SecurityIdentifier Pour(string nomService)
    {
        var empreinte = SHA1.HashData(Encoding.Unicode.GetBytes(nomService.ToUpperInvariant()));

        var sb = new StringBuilder("S-1-5-80");
        for (var i = 0; i < 5; i++)
        {
            sb.Append('-').Append(BitConverter.ToUInt32(empreinte, i * 4));
        }

        return new SecurityIdentifier(sb.ToString());
    }
}