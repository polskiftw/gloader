using System;

#if GLOADER_CLIENT
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;
using Terraria.WorldBuilding;

/// <summary>
/// Current-era Terraria initializes the world-size statue multiplier as:
///
///   Small  = 2 + 0
///   Medium = 2 + 1
///   Large  = 2 + 2
///
/// with the offset selected by maxTilesX thresholds. Expanded worlds satisfy the
/// Large threshold and would therefore remain capped at 4 unless this explicit
/// discrete-tier rule is continued. XL/Huge are the next two UI tiers, so their
/// source continuation is 5/6.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldStatueMultiplierPatch
{
    private static readonly FieldInfo StatueMultiplierField =
        AccessTools.Field(typeof(GenVars), "extraBastStatueCountMax")
        ?? throw new MissingFieldException(typeof(GenVars).FullName, "extraBastStatueCountMax");

    private static readonly MethodInfo AdjustMethod =
        AccessTools.Method(typeof(ExpandedWorldStatueMultiplierPatch), nameof(Adjust))
        ?? throw new MissingMethodException(
            typeof(ExpandedWorldStatueMultiplierPatch).FullName,
            nameof(Adjust));

    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.GetDeclaredMethods(typeof(WorldGen))
            .FirstOrDefault(candidate => candidate.Name == "GenerateWorld" && candidate.IsStatic);

        if (method == null)
            throw new MissingMethodException(typeof(WorldGen).FullName, "GenerateWorld");

        return method;
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        int patched = 0;

        for (int i = 0; i < code.Count; i++)
        {
            if (code[i].opcode != OpCodes.Stsfld || !Equals(code[i].operand, StatueMultiplierField))
                continue;

            // The source-calculated int is already on the stack. Transform it
            // before vanilla stores the field. Move labels/EH boundaries so an
            // incoming branch cannot jump directly to the store and skip us.
            var adjust = new CodeInstruction(OpCodes.Call, AdjustMethod);
            adjust.labels.AddRange(code[i].labels);
            code[i].labels.Clear();
            adjust.blocks.AddRange(code[i].blocks);
            code[i].blocks.Clear();
            code.Insert(i, adjust);
            patched++;
            i++;
        }

        if (patched != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Statue multiplier source shape changed in " +
                (__originalMethod?.DeclaringType?.FullName ?? "WorldGen") + "." +
                (__originalMethod?.Name ?? "GenerateWorld") +
                ": expected exactly one GenVars.extraBastStatueCountMax assignment, found " +
                patched + ". Refusing to guess against this Terraria build.");
        }

        return code;
    }

    private static int Adjust(int vanillaMultiplier)
    {
        if (!ExpandedWorldState.GenerationArmed)
            return vanillaMultiplier;

        // Because the physical widths are both >= Large, current vanilla must
        // have selected Large's multiplier before this continuation runs.
        if (vanillaMultiplier != 4)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Expected vanilla Large statue multiplier 4, got " +
                vanillaMultiplier + ".");
        }

        switch (ExpandedWorldState.GenerationPreset)
        {
            case ExpandedWorldPreset.XL:
                return ExpandedWorldStatueTierMath.Multiplier(4);
            case ExpandedWorldPreset.Huge:
                return ExpandedWorldStatueTierMath.Multiplier(5);
            default:
                return vanillaMultiplier;
        }
    }
}
#endif

/// <summary>
/// Pure source-derived discrete statue multiplier: tiers 1/2/3 are vanilla
/// Small/Medium/Large 2/3/4, therefore tiers 4/5 are XL/Huge 5/6.
/// </summary>
internal static class ExpandedWorldStatueTierMath
{
    public static int Multiplier(int oneBasedWorldTier)
    {
        if (oneBasedWorldTier < 1)
            throw new ArgumentOutOfRangeException(nameof(oneBasedWorldTier));

        return checked(oneBasedWorldTier + 1);
    }
}
