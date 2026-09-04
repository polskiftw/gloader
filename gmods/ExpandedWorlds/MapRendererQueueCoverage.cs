#if GLOADER_CLIENT
using System;
using System.Reflection;
using HarmonyLib;

/// <summary>
/// Terraria 1.4.5.8 initializes MapRenderer.changeQueues with a hard-coded
/// horizontal loop of 0..4. ExpandedWorldMapRendererInitializerPatch enlarges
/// the renderer backing grid for THICC worlds before that initializer runs, so
/// the extra columns exist but retail leaves their queue slots null.
///
/// A null queue prevents map changes in those target columns from reaching the
/// GPU-side fullscreen-map cache, producing large rectangular black holes when
/// the player explores beyond vanilla Large's fifth map-target column.
///
/// Repair only the renderer's transient queue cache after its static
/// initializer. Main.Map (the explored-map data that is saved per player/world)
/// is not replaced or cleared here, and no worldgen state or RNG is touched.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldMapRendererQueueCoveragePatch
{
    private static readonly FieldInfo ChangeQueuesField =
        RequireStaticField("changeQueues");

    private static readonly FieldInfo ChangeRefreshThresholdField =
        RequireStaticField("ChangeRefreshThreshold");

    private static MethodBase TargetMethod()
    {
        Type type = ExpandedWorldMapRendererContract.RequireMapRendererType();
        ConstructorInfo initializer = type.TypeInitializer;
        if (initializer == null)
            throw new MissingMethodException(type.FullName, ".cctor");
        return initializer;
    }

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix()
    {
        Array queues = ChangeQueuesField.GetValue(null) as Array;
        if (queues == null || queues.Rank != 2)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] MapRenderer.changeQueues no longer matches the audited two-dimensional array shape.");
        }

        int columns = queues.GetLength(0);
        int rows = queues.GetLength(1);
        int requiredColumns = ExpandedWorldMapRendererContract.BackingTargetColumns;
        int requiredRows = ExpandedWorldMapRendererContract.BackingTargetRows;
        if (columns < requiredColumns || rows < requiredRows)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] MapRenderer.changeQueues backing grid is too small: " +
                columns + "x" + rows + ", expected at least " +
                requiredColumns + "x" + requiredRows + ".");
        }

        Type queueType = ChangeQueuesField.FieldType.GetElementType();
        if (queueType == null)
            throw new InvalidOperationException("[Expanded Worlds] Could not resolve MapRenderer change-queue element type.");

        ConstructorInfo queueConstructor = AccessTools.Constructor(queueType, new[] { typeof(int) });
        if (queueConstructor == null)
        {
            throw new MissingMethodException(
                queueType.FullName,
                ".ctor(int)");
        }

        object rawCapacity = ChangeRefreshThresholdField.GetValue(null);
        if (!(rawCapacity is int))
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] MapRenderer.ChangeRefreshThreshold no longer matches the audited Int32 shape.");
        }

        int capacity = (int)rawCapacity;
        int repaired = 0;

        for (int x = 0; x < requiredColumns; x++)
        {
            for (int y = 0; y < requiredRows; y++)
            {
                if (queues.GetValue(x, y) != null)
                    continue;

                object queue = queueConstructor.Invoke(new object[] { capacity });
                queues.SetValue(queue, x, y);
                repaired++;
            }
        }

        // Fail closed if the runtime shape changes in a way that leaves any
        // expanded backing cell unusable despite the repair above.
        for (int x = 0; x < requiredColumns; x++)
        {
            for (int y = 0; y < requiredRows; y++)
            {
                if (queues.GetValue(x, y) == null)
                {
                    throw new InvalidOperationException(
                        "[Expanded Worlds] MapRenderer.changeQueues remained null at [" +
                        x + "," + y + "] after coverage repair.");
                }
            }
        }

        Console.WriteLine(
            "[Expanded Worlds] MapRenderer queue coverage verified at " +
            requiredColumns + "x" + requiredRows +
            (repaired > 0 ? "; initialized " + repaired + " expanded queue cell(s)." : "."));
    }

    private static FieldInfo RequireStaticField(string fieldName)
    {
        Type type = ExpandedWorldMapRendererContract.RequireMapRendererType();
        FieldInfo field = AccessTools.Field(type, fieldName);
        if (field == null || !field.IsStatic)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] MapRenderer." + fieldName +
                " no longer matches the audited static field shape.");
        }
        return field;
    }
}
#endif
