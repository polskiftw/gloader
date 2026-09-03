#if GLOADER_CLIENT
using System;

/// <summary>
/// Expanded Worlds deliberately separates its tiny set of must-have runtime
/// patches from source-shape-sensitive biome/count refinements.
///
/// GLoader normally keeps Harmony patching all-or-nothing. This policy opts this
/// mod into per-patch isolation so a changed optional transpiler cannot erase the
/// world-creation UI and core dimension/storage patches that already resolved.
/// </summary>
public static class PatchPolicy
{
    public static bool ShouldPatch(Type patchType)
    {
        if (patchType == null)
            return false;

        switch (patchType.Name)
        {
            // Retired first-generation UI hooks. WorldSizeUiFix.cs owns the
            // current row implementation; patching both versions would install
            // duplicate XL/Huge/THICC controls.
            case "ExpandedWorldCreationSizeRowPatch":
            case "ExpandedWorldVanillaSizeClickPatch":
            case "ExpandedWorldDefaultOptionsPatch":
            case "ExpandedWorldSliderRefreshPatch":
                return false;
            default:
                return true;
        }
    }

    public static bool IsOptional(Type patchType)
    {
        if (patchType == null)
            return false;

        switch (patchType.Name)
        {
            // Current world-creation UI. If any of these cannot patch the exact
            // Terraria client, do not present a half-working custom size picker.
            case "ExpandedWorldBuildPageSizeRowFix":
            case "ExpandedWorldBuildPageVanillaSizeSyncPatch":
            case "ExpandedWorldBuildPageDefaultSyncPatch":
            case "ExpandedWorldBuildPageSliderSyncPatch":
            case "ExpandedWorldCreationDrawGuardPatch":

            // Core generation lifetime/dimension hooks.
            case "ExpandedWorldCreatePatch":
            case "ExpandedWorldClearPatch":
            case "ExpandedWorldGenerateWorldLifetimePatch":

            // Core expanded-canvas storage and client map rendering.
            case "ExpandedWorldBackingStoragePatch":
            case "ExpandedWorldSectionStorageInitializerPatch":
            case "ExpandedWorldMapRendererInitializerPatch":
            case "ExpandedWorldMapRendererDrawPatch":

            // Required scratch capacity before an expanded generation begins.
            case "ExpandedWorldGenerationCapacityPatch":
                return false;
        }

        // Everything else in the client assembly is a source-shape-sensitive
        // scaling/metadata refinement. If one of those optional patches stops
        // matching, keep the core mod alive and log the exact class instead of
        // Harmony rolling the entire Expanded Worlds mod back to vanilla.
        return patchType.Name.StartsWith("ExpandedWorld", StringComparison.Ordinal);
    }
}
#endif
