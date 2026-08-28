using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gelatin.Core.Models;

public static class GelJson
{
    private static readonly JsonSerializerOptions CompactOptions = CreateOptions(false);
    private static readonly JsonSerializerOptions PrettyOptions = CreateOptions(true);

    public static byte[] Serialize(GelConfig config, bool pretty = false)
        => JsonSerializer.SerializeToUtf8Bytes(config, pretty ? PrettyOptions : CompactOptions);

    public static GelConfig Deserialize(ReadOnlySpan<byte> utf8)
    {
        try
        {
            return JsonSerializer.Deserialize<GelConfig>(utf8, CompactOptions)
                ?? throw new GelFormatException("The GEL configuration is empty.");
        }
        catch (GelFormatException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new GelFormatException($"The GEL configuration is not valid JSON: {ex.Message}", ex);
        }
    }

    private static JsonSerializerOptions CreateOptions(bool pretty) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = pretty,
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowTrailingCommas = false
    };
}
