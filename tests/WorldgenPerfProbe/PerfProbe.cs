#if GLOADER
using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Terraria;
using Terraria.WorldBuilding;

public static class Mod
{
    public static void Load()
    {
        Console.WriteLine(
            "[Worldgen Perf] runtime=" + GetMonoDisplayName() +
            "; env_version=" + Environment.Version +
            "; processors=" + Environment.ProcessorCount +
            "; mono_options=" + (Environment.GetEnvironmentVariable("MONO_BUNDLED_OPTIONS") ?? "<unset>"));
    }

    private static string GetMonoDisplayName()
    {
        try
        {
            Type runtime = typeof(object).Assembly.GetType("Mono.Runtime", false);
            if (runtime == null)
                return "<not-mono>";

            MethodInfo method = runtime.GetMethod(
                "GetDisplayName",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            return method == null
                ? "<mono-version-unavailable>"
                : (method.Invoke(null, null) as string ?? "<mono-version-unavailable>");
        }
        catch (Exception ex)
        {
            return "<mono-version-error:" + ex.GetType().Name + ">";
        }
    }
}

[HarmonyPatch(typeof(WorldGen), "clearWorld")]
internal static class WorldgenPerfClearWorldPatch
{
    private sealed class State
    {
        internal long WallTicks;
        internal TimeSpan Cpu;
        internal int Gen0;
        internal int Gen1;
        internal int Gen2;
    }

    [HarmonyPrefix]
    private static void Prefix(out State __state)
    {
        __state = new State
        {
            WallTicks = Stopwatch.GetTimestamp(),
            Cpu = CurrentCpu(),
            Gen0 = GC.CollectionCount(0),
            Gen1 = GC.CollectionCount(1),
            Gen2 = GC.CollectionCount(2)
        };

        Console.WriteLine(
            "[Worldgen Perf] clearWorld_begin dimensions=" +
            Main.maxTilesX + "x" + Main.maxTilesY);
    }

    [HarmonyPostfix]
    private static void Postfix(State __state)
    {
        if (__state == null)
            return;

        long endTicks = Stopwatch.GetTimestamp();
        TimeSpan endCpu = CurrentCpu();
        double wallMs =
            (endTicks - __state.WallTicks) * 1000.0 / Stopwatch.Frequency;
        double cpuMs = (endCpu - __state.Cpu).TotalMilliseconds;

        long managed = 0;
        long workingSet = 0;
        long privateBytes = 0;
        try
        {
            managed = GC.GetTotalMemory(false);
            using (Process process = Process.GetCurrentProcess())
            {
                workingSet = process.WorkingSet64;
                privateBytes = process.PrivateMemorySize64;
            }
        }
        catch
        {
        }

        Console.WriteLine(
            "[Worldgen Perf] clearWorld_end dimensions=" +
            Main.maxTilesX + "x" + Main.maxTilesY +
            "; wall_ms=" + wallMs.ToString("F1") +
            "; cpu_ms=" + cpuMs.ToString("F1") +
            "; gc0=" + (GC.CollectionCount(0) - __state.Gen0) +
            "; gc1=" + (GC.CollectionCount(1) - __state.Gen1) +
            "; gc2=" + (GC.CollectionCount(2) - __state.Gen2) +
            "; managed_mib=" + (managed / 1048576.0).ToString("F1") +
            "; rss_mib=" + (workingSet / 1048576.0).ToString("F1") +
            "; private_mib=" + (privateBytes / 1048576.0).ToString("F1"));

        if (string.Equals(
            Environment.GetEnvironmentVariable("GLOADER_PERF_EXIT_AFTER_CLEARWORLD"),
            "1",
            StringComparison.Ordinal))
        {
            Console.Out.Flush();
            Console.Error.Flush();
            Environment.Exit(0);
        }
    }

    private static TimeSpan CurrentCpu()
    {
        try
        {
            using (Process process = Process.GetCurrentProcess())
                return process.TotalProcessorTime;
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }
}

[HarmonyPatch(typeof(GenPass), nameof(GenPass.Apply))]
internal static class WorldgenPerfPassPatch
{
    private sealed class State
    {
        internal string Name;
        internal long WallTicks;
        internal TimeSpan Cpu;
        internal int Gen0;
        internal int Gen1;
        internal int Gen2;
    }

    [HarmonyPrefix]
    private static void Prefix(GenPass __instance, out State __state)
    {
        __state = new State
        {
            Name = __instance == null ? "<unknown>" : (__instance.Name ?? "<unnamed>"),
            WallTicks = Stopwatch.GetTimestamp(),
            Cpu = CurrentCpu(),
            Gen0 = GC.CollectionCount(0),
            Gen1 = GC.CollectionCount(1),
            Gen2 = GC.CollectionCount(2)
        };
    }

    [HarmonyPostfix]
    private static void Postfix(State __state)
    {
        if (__state == null)
            return;

        long endTicks = Stopwatch.GetTimestamp();
        TimeSpan endCpu = CurrentCpu();

        double wallMs =
            (endTicks - __state.WallTicks) * 1000.0 / Stopwatch.Frequency;
        double cpuMs = (endCpu - __state.Cpu).TotalMilliseconds;
        double oneCorePercent = wallMs <= 0.0 ? 0.0 : cpuMs * 100.0 / wallMs;

        long managed = 0;
        long workingSet = 0;
        try
        {
            managed = GC.GetTotalMemory(false);
            using (Process process = Process.GetCurrentProcess())
                workingSet = process.WorkingSet64;
        }
        catch
        {
        }

        Console.WriteLine(
            "[Worldgen Perf] pass=" + __state.Name +
            "; wall_ms=" + wallMs.ToString("F1") +
            "; cpu_ms=" + cpuMs.ToString("F1") +
            "; cpu_one_core_pct=" + oneCorePercent.ToString("F1") +
            "; gc0=" + (GC.CollectionCount(0) - __state.Gen0) +
            "; gc1=" + (GC.CollectionCount(1) - __state.Gen1) +
            "; gc2=" + (GC.CollectionCount(2) - __state.Gen2) +
            "; managed_mib=" + (managed / 1048576.0).ToString("F1") +
            "; rss_mib=" + (workingSet / 1048576.0).ToString("F1"));
    }

    private static TimeSpan CurrentCpu()
    {
        try
        {
            using (Process process = Process.GetCurrentProcess())
                return process.TotalProcessorTime;
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }
}
#endif
