#if GLOADER_CLIENT
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Terraria.GameContent.UI.States;
using Terraria.UI;

/// <summary>
/// Last-resort retail UI guard for Expanded Worlds.
///
/// UIWorldCreation normally builds its page after gloader installs Harmony
/// patches, so WorldSizeUiFix.cs can inject XL/Huge/THICC from BuildPage(). If
/// Terraria has already constructed that UI state before the mod patch set is
/// installed, however, the BuildPage postfix cannot retroactively run. Draw is
/// guaranteed to execute when the New World page is actually shown, so this
/// guard performs one idempotent recovery attempt for that live UI instance.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldCreationDrawGuardPatch
{
    private static readonly MethodInfo InjectMethod =
        AccessTools.Method(
            typeof(ExpandedWorldBuildPageSizeRowFix),
            "Inject",
            new[] { typeof(UIWorldCreation) });

    private static readonly FieldInfo XlButtonField =
        AccessTools.Field(typeof(ExpandedWorldBuildPageSizeRowFix), "_xlButton");

    private static readonly PropertyInfo ParentProperty =
        AccessTools.Property(typeof(UIElement), "Parent");

    private static UIWorldCreation _lastRecoveryOwner;

    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.GetDeclaredMethods(typeof(UIWorldCreation))
            .FirstOrDefault(candidate =>
                candidate.Name == "Draw" &&
                candidate.GetParameters().Length == 1);

        if (method == null)
            throw new MissingMethodException(typeof(UIWorldCreation).FullName, "Draw(SpriteBatch)");

        return method;
    }

    [HarmonyPrefix]
    private static void Prefix(UIWorldCreation __instance)
    {
        if (__instance == null || IsInstalled())
            return;

        // Do not hammer reflection every frame if something genuinely fails.
        // A rebuilt UIWorldCreation instance gets its own recovery attempt.
        if (ReferenceEquals(__instance, _lastRecoveryOwner))
            return;

        _lastRecoveryOwner = __instance;

        try
        {
            if (InjectMethod == null)
                throw new MissingMethodException(
                    typeof(ExpandedWorldBuildPageSizeRowFix).FullName,
                    "Inject(UIWorldCreation)");

            InjectMethod.Invoke(null, new object[] { __instance });
            Console.WriteLine("[Expanded Worlds] Draw guard verified the custom world-size row.");
        }
        catch (TargetInvocationException ex)
        {
            Exception inner = ex.InnerException ?? ex;
            Console.WriteLine("[Expanded Worlds] Draw guard could not recover world-size buttons: " + inner);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[Expanded Worlds] Draw guard could not recover world-size buttons: " + ex);
        }
    }

    private static bool IsInstalled()
    {
        if (XlButtonField == null || ParentProperty == null)
            return false;

        try
        {
            UIElement button = XlButtonField.GetValue(null) as UIElement;
            return button != null && ParentProperty.GetValue(button, null) != null;
        }
        catch
        {
            return false;
        }
    }
}
#endif
