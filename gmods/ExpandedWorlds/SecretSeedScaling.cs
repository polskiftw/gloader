using System;

#if GLOADER_CLIENT
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;
#endif

/// <summary>
/// Aspect-ratio repairs for special/secret-seed generation rules.
///
/// The seed remains authoritative: these helpers do not add/remove seed
/// branches or replace their control flow. They only replace a vanilla
/// dimension proxy once Expanded Worlds deliberately breaks the normal
/// width/height relationship.
/// </summary>
internal static class ExpandedWorldSecretSeedMath
{
    /// <summary>
    /// Terraria 1.4.5.8 Don't Starve Wavy Caves computes:
    ///   scale = maxTilesX / 4200.0
    ///   count = floor(35 * scale * scale)
    ///
    /// On vanilla sizes, width roughly tracks both dimensions and width^2 acts
    /// as an area proxy. Expanded Worlds keeps Large's 2400-tile height while
    /// increasing only width, so continuing width^2 would make cave density
    /// rise again merely because the map is wider.
    ///
    /// Preserve Terraria's exact Small/Medium/Large source result, then continue
    /// beyond Large from Large's exact 140-cave baseline by physical tile area.
    /// This gives XL=210 and Huge=280 instead of the raw width^2 values 315/560.
    /// </summary>
    public static int DontStarveWavyCaveBaseCount(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        if (width <= ExpandedWorldMath.LargeWidth)
        {
            double sourceScale = width / (double)ExpandedWorldMath.SmallWidth;
            return (int)(35d * sourceScale * sourceScale);
        }

        const int vanillaLargeBaseCount = 140;
        return (int)(vanillaLargeBaseCount * ExpandedWorldMath.AreaRelativeToLarge(width, height));
    }

    public static int DontStarveWavyCaveCount(int width, int height, bool remixWorld)
    {
        int count = DontStarveWavyCaveBaseCount(width, height);

        // This is Terraria's seed interaction and intentionally stays after the
        // aspect-ratio repair, exactly where vanilla applies it.
        if (remixWorld)
            count /= 3;

        return count;
    }
}

#if GLOADER_CLIENT
/// <summary>
/// Repairs only the count scalar inside Terraria's Don't Starve Wavy Caves
/// generation-pass delegate. The delegate itself, Remix /3 branch, RNG calls,
/// positions, WavyCaverer parameters, and every other seed interaction remain
/// vanilla-owned.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldDontStarveWavyCavesPatch
{
    private static readonly FieldInfo MaxTilesXField =
        AccessTools.Field(typeof(Main), nameof(Main.maxTilesX))
        ?? throw new MissingFieldException(typeof(Main).FullName, nameof(Main.maxTilesX));

    private static readonly MethodInfo WavyCavererMethod =
        AccessTools.Method(typeof(WorldGen), "WavyCaverer")
        ?? throw new MissingMethodException(typeof(WorldGen).FullName, "WavyCaverer");

    private static readonly MethodInfo AdjustCountMethod =
        AccessTools.Method(typeof(ExpandedWorldDontStarveWavyCavesPatch), nameof(AdjustBaseCount))
        ?? throw new MissingMethodException(
            typeof(ExpandedWorldDontStarveWavyCavesPatch).FullName,
            nameof(AdjustBaseCount));

    private static MethodBase TargetMethod()
    {
        List<MethodBase> matches = EnumerateImplementationMethods(typeof(WorldGen))
            .Where(ContainsWavyCavesSourceShape)
            .ToList();

        if (matches.Count != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Could not uniquely resolve the Don't Starve Wavy Caves generation delegate: " +
                "expected one implementation containing maxTilesX, 4200.0, 35.0 and WavyCaverer, found " +
                matches.Count + ". Refusing to guess against this Terraria build.");
        }

        return matches[0];
    }

    private static IEnumerable<MethodBase> EnumerateImplementationMethods(Type root)
    {
        const BindingFlags methodFlags =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Static | BindingFlags.Instance |
            BindingFlags.DeclaredOnly;

        foreach (MethodInfo method in root.GetMethods(methodFlags))
            yield return method;

        const BindingFlags nestedFlags = BindingFlags.Public | BindingFlags.NonPublic;
        foreach (Type nested in root.GetNestedTypes(nestedFlags))
        {
            foreach (MethodBase method in EnumerateImplementationMethods(nested))
                yield return method;
        }
    }

    private static bool ContainsWavyCavesSourceShape(MethodBase method)
    {
        byte[] il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch
        {
            return false;
        }

        if (il == null || il.Length == 0)
            return false;

        return ContainsFieldLoad(il, MaxTilesXField.MetadataToken) &&
               ContainsCall(il, WavyCavererMethod.MetadataToken) &&
               ContainsDouble(il, 4200d) &&
               ContainsDouble(il, 35d);
    }

    private static bool ContainsFieldLoad(byte[] il, int metadataToken)
    {
        for (int i = 0; i + 4 < il.Length; i++)
        {
            // ldsfld
            if (il[i] == 0x7e && BitConverter.ToInt32(il, i + 1) == metadataToken)
                return true;
        }

        return false;
    }

    private static bool ContainsCall(byte[] il, int metadataToken)
    {
        for (int i = 0; i + 4 < il.Length; i++)
        {
            if ((il[i] == 0x28 || il[i] == 0x6f) && BitConverter.ToInt32(il, i + 1) == metadataToken)
                return true;
        }

        return false;
    }

    private static bool ContainsDouble(byte[] il, double value)
    {
        byte[] expected = BitConverter.GetBytes(value);
        for (int i = 0; i + 8 < il.Length; i++)
        {
            // ldc.r8
            if (il[i] != 0x23)
                continue;

            bool equal = true;
            for (int j = 0; j < 8; j++)
            {
                if (il[i + 1 + j] != expected[j])
                {
                    equal = false;
                    break;
                }
            }

            if (equal)
                return true;
        }

        return false;
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        int patched = 0;

        // Decompiled 1.4.5.8 source:
        //   double scale = maxTilesX / 4200.0;
        //   scale *= scale;
        //   int count = (int)(35.0 * scale);
        //   if (Main.remixWorld) count /= 3;
        //
        // Inject after the conversion to int and before its local store. Remix's
        // own divisor remains downstream and therefore remains entirely vanilla.
        for (int i = 0; i < code.Count; i++)
        {
            if (!IsDoubleConstant(code[i], 35d))
                continue;

            bool sawMultiply = false;
            bool sawConvert = false;
            int end = Math.Min(code.Count, i + 10);

            for (int j = i + 1; j < end; j++)
            {
                sawMultiply |= code[j].opcode == OpCodes.Mul;
                sawConvert |= code[j].opcode == OpCodes.Conv_I4;

                if (!sawMultiply || !sawConvert || !IsLocalStore(code[j]))
                    continue;

                var adjust = new CodeInstruction(OpCodes.Call, AdjustCountMethod);
                adjust.labels.AddRange(code[j].labels);
                code[j].labels.Clear();
                adjust.blocks.AddRange(code[j].blocks);
                code[j].blocks.Clear();
                code.Insert(j, adjust);
                patched++;
                i = j + 1;
                break;
            }
        }

        if (patched != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Don't Starve Wavy Caves source shape changed in " +
                (__originalMethod?.DeclaringType?.FullName ?? "WorldGen") + "." +
                (__originalMethod?.Name ?? "<generated>") +
                ": expected exactly one 35.0*scale count conversion, found " +
                patched + ". Refusing to guess against this Terraria build.");
        }

        return code;
    }

    private static int AdjustBaseCount(int vanillaCount)
    {
        if (!ExpandedWorldState.GenerationArmed || Main.maxTilesX <= ExpandedWorldMath.LargeWidth)
            return vanillaCount;

        double sourceScale = Main.maxTilesX / (double)ExpandedWorldMath.SmallWidth;
        int expectedVanillaCount = (int)(35d * sourceScale * sourceScale);
        if (vanillaCount != expectedVanillaCount)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Expected Terraria 1.4.5.8 Don't Starve Wavy Caves base count " +
                expectedVanillaCount + " at width " + Main.maxTilesX +
                ", got " + vanillaCount + ". Refusing to infer a changed source rule.");
        }

        return ExpandedWorldSecretSeedMath.DontStarveWavyCaveBaseCount(
            Main.maxTilesX,
            Main.maxTilesY);
    }

    private static bool IsDoubleConstant(CodeInstruction instruction, double expected)
    {
        return instruction.opcode == OpCodes.Ldc_R8 &&
               instruction.operand is double &&
               (double)instruction.operand == expected;
    }

    private static bool IsLocalStore(CodeInstruction instruction)
    {
        return instruction.opcode == OpCodes.Stloc_0 ||
               instruction.opcode == OpCodes.Stloc_1 ||
               instruction.opcode == OpCodes.Stloc_2 ||
               instruction.opcode == OpCodes.Stloc_3 ||
               instruction.opcode == OpCodes.Stloc ||
               instruction.opcode == OpCodes.Stloc_S;
    }
}
#endif
