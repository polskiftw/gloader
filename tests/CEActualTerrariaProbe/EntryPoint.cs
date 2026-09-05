using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GLoaderCeTerrariaProbe;

public static class EntryPoint
{
    private const int SuccessReturn = 23063;
    private static readonly BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static int Initialize(string outputPath)
    {
        outputPath = Path.GetFullPath(outputPath);
        var thread = new Thread(() => Probe(outputPath))
        {
            IsBackground = true,
            Name = "CE Terraria reflection probe"
        };
        thread.Start();
        return SuccessReturn;
    }

    private static void Probe(string outputPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            var stopwatch = Stopwatch.StartNew();
            Assembly? gameAssembly = null;
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(45))
            {
                gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => SafeGetType(a, "Terraria.Main") is not null &&
                                         SafeGetType(a, "Terraria.Player") is not null &&
                                         SafeGetType(a, "Terraria.NPC") is not null);
                if (gameAssembly is not null)
                    break;

                Thread.Sleep(10);
            }

            if (gameAssembly is null)
                throw new InvalidOperationException("Terraria managed assembly did not appear in the gloader AppDomain within 45 seconds.");

            var main = RequireType(gameAssembly, "Terraria.Main");
            var npc = RequireType(gameAssembly, "Terraria.NPC");
            var player = RequireType(gameAssembly, "Terraria.Player");
            var projectile = RequireType(gameAssembly, "Terraria.Projectile");
            var entitySource = RequireType(gameAssembly, "Terraria.DataStructures.IEntitySource");

            var mainNpc = main.GetField("npc", AnyStatic);
            var mainMyPlayer = main.GetField("myPlayer", AnyStatic);

            var npcCheckDead = FindMethod(npc, "checkDead", AnyInstance, p => p.Length == 0);
            var npcStrikeInstantKill = FindMethod(npc, "StrikeInstantKill", AnyInstance, p => p.Length == 0);
            var npcPlayerInteraction = FindMethod(npc, "PlayerInteraction", AnyInstance,
                p => p.Length == 1 && p[0].ParameterType == typeof(int));

            var fishMethod = projectile.GetMethods(AnyInstance | BindingFlags.Static)
                .Where(m => m.Name == "FishingCheck_RollDropLevels")
                .FirstOrDefault(m =>
                {
                    var p = m.GetParameters();
                    return p.Length > 0 && p[^1].ParameterType == typeof(bool).MakeByRefType();
                });

            var openBossBag = FindMethod(player, "OpenBossBag", AnyInstance,
                p => p.Length == 1 && p[0].ParameterType == typeof(int));
            var quickSpawnItem = FindMethod(player, "QuickSpawnItem", AnyInstance,
                p => p.Length == 3 &&
                     p[0].ParameterType == entitySource &&
                     p[1].ParameterType == typeof(int) &&
                     p[2].ParameterType == typeof(int));
            var tryGettingDevArmor = FindMethod(player, "TryGettingDevArmor", AnyInstance,
                p => p.Length == 1 && p[0].ParameterType == entitySource);
            var getItemSourceOpenItem = FindMethod(player, "GetItemSource_OpenItem", AnyInstance,
                p => p.Length == 1 && p[0].ParameterType == typeof(int));

            var deathAuraReady =
                mainNpc is not null && mainNpc.FieldType.IsArray &&
                mainMyPlayer?.FieldType == typeof(int) &&
                npcPlayerInteraction is not null &&
                (npcCheckDead is not null || npcStrikeInstantKill is not null);

            var allFishAreCratesReady = fishMethod is not null;

            var luckyTreasureBagsReady =
                openBossBag is not null &&
                quickSpawnItem is not null &&
                tryGettingDevArmor is not null &&
                getItemSourceOpenItem is not null;

            var lines = new List<string>
            {
                "Status=SUCCESS",
                $"ProcessId={Environment.ProcessId}",
                $"Framework={RuntimeInformation.FrameworkDescription}",
                $"Architecture={RuntimeInformation.ProcessArchitecture}",
                $"AssemblyName={gameAssembly.GetName().Name}",
                $"AssemblyFullName={gameAssembly.FullName}",
                $"AssemblyLocation={SafeLocation(gameAssembly)}",
                $"MainNpcField={FieldSignature(mainNpc)}",
                $"MainMyPlayerField={FieldSignature(mainMyPlayer)}",
                $"NpcCheckDead={MethodSignature(npcCheckDead)}",
                $"NpcStrikeInstantKill={MethodSignature(npcStrikeInstantKill)}",
                $"NpcPlayerInteraction={MethodSignature(npcPlayerInteraction)}",
                $"FishingCheckRollDropLevels={MethodSignature(fishMethod)}",
                $"OpenBossBag={MethodSignature(openBossBag)}",
                $"QuickSpawnItem={MethodSignature(quickSpawnItem)}",
                $"TryGettingDevArmor={MethodSignature(tryGettingDevArmor)}",
                $"GetItemSourceOpenItem={MethodSignature(getItemSourceOpenItem)}",
                $"DeathAuraReady={deathAuraReady}",
                $"AllFishAreCratesReady={allFishAreCratesReady}",
                $"LuckyTreasureBagsReady={luckyTreasureBagsReady}",
                $"OverallReady={deathAuraReady && allFishAreCratesReady && luckyTreasureBagsReady}"
            };

            File.WriteAllLines(outputPath, lines);
        }
        catch (Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllText(
                    outputPath,
                    $"Status=FAIL{Environment.NewLine}" +
                    $"ProcessId={Environment.ProcessId}{Environment.NewLine}" +
                    $"Framework={RuntimeInformation.FrameworkDescription}{Environment.NewLine}" +
                    $"Architecture={RuntimeInformation.ProcessArchitecture}{Environment.NewLine}" +
                    ex + Environment.NewLine);
            }
            catch
            {
                // The host may be terminating; there is nowhere else useful to report.
            }
        }
    }

    private static Type? SafeGetType(Assembly assembly, string name)
    {
        try { return assembly.GetType(name, throwOnError: false, ignoreCase: false); }
        catch { return null; }
    }

    private static Type RequireType(Assembly assembly, string name) =>
        assembly.GetType(name, throwOnError: false, ignoreCase: false)
        ?? throw new MissingMemberException($"Required Terraria type was not found: {name}");

    private static MethodInfo? FindMethod(Type type, string name, BindingFlags flags, Func<ParameterInfo[], bool> match) =>
        type.GetMethods(flags).Where(m => m.Name == name).FirstOrDefault(m => match(m.GetParameters()));

    private static string MethodSignature(MethodInfo? method)
    {
        if (method is null)
            return "<missing>";

        var parameters = string.Join(", ", method.GetParameters().Select(p =>
            (p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : string.Empty) +
            FriendlyTypeName(p.ParameterType) + " " + p.Name));
        return $"{FriendlyTypeName(method.ReturnType)} {method.DeclaringType?.FullName}.{method.Name}({parameters})";
    }

    private static string FieldSignature(FieldInfo? field) =>
        field is null ? "<missing>" : $"{FriendlyTypeName(field.FieldType)} {field.DeclaringType?.FullName}.{field.Name}";

    private static string FriendlyTypeName(Type type)
    {
        if (type.IsByRef)
            return FriendlyTypeName(type.GetElementType()!) + "&";
        if (type.IsArray)
            return FriendlyTypeName(type.GetElementType()!) + "[]";
        return type.FullName ?? type.Name;
    }

    private static string SafeLocation(Assembly assembly)
    {
        try { return assembly.Location; }
        catch { return "<unavailable>"; }
    }
}
