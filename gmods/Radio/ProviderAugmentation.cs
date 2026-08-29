#if !GLOADER_SERVER
using System;
using System.Linq;
using System.Threading;

internal static class RadioProviderAugmentation
{
    private static int _discoveryStarted;

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
        if (Interlocked.Exchange(ref _discoveryStarted, 1) != 0) return;
        new Thread(() =>
        {
            try
            {
                var current113 = Radio113Fm.Discover();
                if (current113.Count > 0) RadioCatalog.AddDirectoryResults(current113);
            }
            catch
            {
                // Catalog refresh is best-effort. Built-ins and cached catalogs remain
                // usable if a provider cannot be enumerated during this launch.
            }
        })
        {
            IsBackground = true,
            Name = "gloader Radio provider discovery"
        }.Start();
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
