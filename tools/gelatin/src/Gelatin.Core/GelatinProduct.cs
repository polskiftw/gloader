using System.Reflection;

namespace Gelatin.Core;

public static class GelatinProduct
{
    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var informational = typeof(GelatinProduct).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
            return typeof(GelatinProduct).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var metadata = informational.IndexOf('+');
        return metadata >= 0 ? informational[..metadata] : informational;
    }
}
