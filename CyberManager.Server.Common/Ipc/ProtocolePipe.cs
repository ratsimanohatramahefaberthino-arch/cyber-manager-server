using System.Text.Json;
using System.Text.Json.Serialization;

namespace CyberManager.Server.Common.Ipc;

/// <summary>Sérialisation JSON « une ligne par message » du pipe (utilisée par le service et par la télécommande).</summary>
public static class ProtocolePipe
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static string EncoderRequete(PipeRequete r) => JsonSerializer.Serialize(r, Options);

    public static string EncoderReponse(PipeReponse r) => JsonSerializer.Serialize(r, Options);

    public static PipeRequete? DecoderRequete(string ligne)
    {
        try { return JsonSerializer.Deserialize<PipeRequete>(ligne, Options); }
        catch { return null; }
    }

    public static PipeReponse? DecoderReponse(string ligne)
    {
        try { return JsonSerializer.Deserialize<PipeReponse>(ligne, Options); }
        catch { return null; }
    }
}