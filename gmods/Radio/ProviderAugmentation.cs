#if !GLOADER_SERVER
using System;
using System.Linq;

internal static class RadioProviderAugmentation
{
    internal static void ApplyStaticFallbacks()
    {
        var sceneSat = RadioCatalog.Find("scenesat:main");
        if (sceneSat != null)
        {
            AddStreamIfMissing(sceneSat, RadioCatalog.Variant("http://SC.SceneSat.com:8000", "mp3", 128, "direct", "128k MP3 fallback"));
            AddStreamIfMissing(sceneSat, RadioCatalog.Variant("http://Oscar.SceneSat.com:8000/scenesat", "mp3", 128, "direct", "128k MP3 fallback"));
            AddStreamIfMissing(sceneSat, RadioCatalog.Variant("", "mp3", 0, "radio-browser-exact", "Public directory fallback", "SceneSat"));
        }
    }

    internal static void BeginBackgroundDiscovery()
    {
        // RadioCatalog.BeginRefresh owns all provider-wide enumeration, including the
        // metadata-validating 113.FM scan. Keep this entrypoint for Main.cs compatibility
        // but do not launch a second copy of that expensive scan.
    }

    private static void AddStreamIfMissing(Station station, StreamVariant variant)
    {
        if (station == null || variant == null) return;
        var duplicate = station.Streams.Any(existing =>
            string.Equals(existing.Url ?? string.Empty, variant.Url ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.Resolver ?? string.Empty, variant.Resolver ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.ResolverArgument ?? string.Empty, variant.ResolverArgument ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        if (!duplicate) station.Streams.Add(variant);
    }
}
#endif
