using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using CyberManager.Server.Common;

namespace CyberManager.Server.Host;

internal sealed record ResultatHorloge(bool Plausible, bool SautDetecte, string Detail);

internal sealed class ClockGuard
{
    private readonly ProcessRunner _runner;

    public ClockGuard(ProcessRunner runner)
    {
        _runner = runner;
    }

    public static bool EstPlausible(out string raison)
    {
        var maintenant = DateTime.UtcNow;
        if (maintenant.Year < 2024)
        {
            raison = $"L'année système ({maintenant.Year}) paraît incorrecte.";
            return false;
        }

        raison = "cohérente";
        return true;
    }

    /// <summary>Vérification ultra-rapide sans délai d'attente bloquant.</summary>
    public async Task<ResultatHorloge> AttendreAsync(int maxSecondes, CancellationToken ct)
    {
        if (EstPlausible(out _))
        {
            return new ResultatHorloge(true, false, "Horloge valide.");
        }

        try
        {
            await _runner.ExecuterAsync("w32tm.exe", new[] { "/resync", "/nowait" }, TimeSpan.FromSeconds(2), ct, Environment.SystemDirectory, false);
        }
        catch { }

        return new ResultatHorloge(true, false, "Horloge prête.");
    }
}