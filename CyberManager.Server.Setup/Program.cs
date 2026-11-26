using System.Text;
using CyberManager.Server.Common;

namespace CyberManager.Server.Setup;

/// <summary>
/// CyberManagerSetupTool.exe : outil d'accompagnement de l'installateur (jamais lancé à la main par le gérant).
///   detect         [--version-cible=X.Y.Z] [--ports=80,8080,...] [--format=json|ini] [--sortie=fichier]
///   probe-port     --port=N | --ports=N,M,...                    [--format=json|ini] [--sortie=fichier]
///   mise-en-place  [--etape=toutes|arreter,dossiers,configuration,taches,parefeu,base,sauvegarde,service,veille,demarrer]
///                  [--demande=fichier.json] [--garder-demande]   [--format=json|ini] [--sortie=fichier]
///   version
/// Option commune : --encodage=utf16|utf8 (format ini ; utf16 par défaut, utf8 pour Inno Setup).
/// Codes de sortie : 0 = succès, 3 = au moins une étape de mise en place a échoué (voir le rapport),
///                   2 = usage invalide, 1 = erreur interne.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);

        try
        {
            var a = Arguments.Analyser(args);

            var encodage = (a.Valeur("encodage") ?? "utf16").ToLowerInvariant();
            if (encodage != "utf16" && encodage != "utf8")
            {
                throw new ArgumentException("--encodage doit valoir utf16 ou utf8.");
            }

            Sortie.EncodageIni = encodage;

            return a.Commande switch
            {
                "detect" => Commandes.Detecter(a),
                "probe-port" => Commandes.SonderPorts(a),
                "mise-en-place" => CommandeMiseEnPlace.Executer(a),
                "version" => Commandes.Version(),
                _ => Usage()
            };
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Erreur interne : " + ex.Message);
            return 1;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage : CyberManagerSetupTool detect|probe-port|mise-en-place|version [options] (voir l'en-tête de Program.cs)");
        return 2;
    }
}

internal sealed class Arguments
{
    public string Commande { get; private init; } = "";
    private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);

    public string? Valeur(string nom) => _options.TryGetValue(nom, out var v) ? v : null;

    public bool Present(string nom) => _options.ContainsKey(nom);

    public static Arguments Analyser(string[] args)
    {
        var a = new Arguments { Commande = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "" };

        for (var i = 1; i < args.Length; i++)
        {
            var t = args[i];
            if (!t.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Argument inattendu : {t}");
            }

            var corps = t[2..];
            var egal = corps.IndexOf('=');
            if (egal > 0)
            {
                a._options[corps[..egal]] = corps[(egal + 1)..].Trim('"');
            }
            else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                a._options[corps] = args[++i].Trim('"');
            }
            else
            {
                a._options[corps] = "1";
            }
        }

        return a;
    }
}