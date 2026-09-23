using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace GLoader
{
    internal static class DeferredModBootstrap
    {
        private const string HarmonyId = "gloader.core.deferred-mods";
        private static readonly object Gate = new object();

        private static Harmony _harmony;
        private static MethodInfo _runGameMethod;
        private static string _modsDirectory;
        private static Assembly _gameAssembly;
        private static string _root;
        private static string _dependencies;
        private static AssemblyResolver _resolver;
        private static bool _isServerTarget;
        private static bool _installed;
        private static bool _loaded;

        public static void Install(
            string modsDirectory,
            Assembly gameAssembly,
            string root,
            string dependencies,
            AssemblyResolver resolver,
            bool isServerTarget)
        {
            lock (Gate)
            {
                if (_installed)
                    return;

                if (gameAssembly == null)
                    throw new ArgumentNullException(nameof(gameAssembly));
                if (resolver == null)
                    throw new ArgumentNullException(nameof(resolver));

                var programType = gameAssembly.GetType("Terraria.Program", true);
                var launchGameMethod = programType.GetMethod(
                    "LaunchGame",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new[] { typeof(string[]), typeof(bool) },
                    null);
                _runGameMethod = programType.GetMethod(
                    "RunGame",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    Type.EmptyTypes,
                    null);

                if (launchGameMethod == null)
                    throw new MissingMethodException(programType.FullName, "LaunchGame(string[],bool)");
                if (_runGameMethod == null)
                    throw new MissingMethodException(programType.FullName, "RunGame()");

                _modsDirectory = modsDirectory;
                _gameAssembly = gameAssembly;
                _root = root;
                _dependencies = dependencies;
                _resolver = resolver;
                _isServerTarget = isServerTarget;

                _harmony = new Harmony(HarmonyId);
                _harmony.Patch(
                    launchGameMethod,
                    transpiler: new HarmonyMethod(
                        typeof(DeferredModBootstrap),
                        nameof(LaunchGameTranspiler)));

                _installed = true;
                Log.Info(
                    "Source mod loading deferred until Terraria LaunchGame startup setup is complete.");
            }
        }

        public static void LoadMods()
        {
            lock (Gate)
            {
                if (_loaded)
                    return;

                _loaded = true;
            }

            try
            {
                Log.Info("Terraria startup setup complete; loading source mods.");

                ModRuntime.LoadAll(
                    _modsDirectory,
                    _gameAssembly,
                    _root,
                    _dependencies,
                    _resolver,
                    _isServerTarget);
            }
            catch (Exception ex)
            {
                Log.Error(
                    "Deferred source-mod loading failed." +
                    Environment.NewLine +
                    ex);
            }
        }

        private static IEnumerable<CodeInstruction> LaunchGameTranspiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            var code = instructions.ToList();
            var callback = AccessTools.Method(
                typeof(DeferredModBootstrap),
                nameof(LoadMods),
                Type.EmptyTypes);

            if (_runGameMethod == null)
                throw new InvalidOperationException("Deferred RunGame target was not initialized.");
            if (callback == null)
                throw new MissingMethodException(
                    typeof(DeferredModBootstrap).FullName,
                    nameof(LoadMods));

            int inserted = 0;

            for (int i = 0; i < code.Count; i++)
            {
                var instruction = code[i];
                if ((instruction.opcode != OpCodes.Call &&
                     instruction.opcode != OpCodes.Callvirt) ||
                    !Equals(instruction.operand, _runGameMethod))
                {
                    continue;
                }

                var loadMods = new CodeInstruction(OpCodes.Call, callback);
                loadMods.labels.AddRange(instruction.labels);
                instruction.labels.Clear();
                loadMods.blocks.AddRange(instruction.blocks);
                instruction.blocks.Clear();

                code.Insert(i, loadMods);
                inserted++;
                i++;
            }

            if (inserted != 1)
            {
                throw new InvalidOperationException(
                    "Terraria Program.LaunchGame startup shape changed in " +
                    (__originalMethod?.DeclaringType?.FullName ?? "Terraria.Program") +
                    ": expected exactly one RunGame() call, found " + inserted + ".");
            }

            return code;
        }
    }
}
