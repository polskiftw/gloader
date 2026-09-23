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
        private static EventInfo _enginePreloadEvent;
        private static Action _enginePreloadHandler;
        private static string _modsDirectory;
        private static Assembly _gameAssembly;
        private static string _root;
        private static string _dependencies;
        private static AssemblyResolver _resolver;
        private static bool _isServerTarget;
        private static bool _installed;
        private static bool _clientArmed;
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

                if (!isServerTarget)
                {
                    var mainType = gameAssembly.GetType("Terraria.Main", true);
                    _enginePreloadEvent = mainType.GetEvent(
                        "OnEnginePreload",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                    if (_enginePreloadEvent == null ||
                        _enginePreloadEvent.EventHandlerType != typeof(Action))
                    {
                        throw new MissingMemberException(
                            mainType.FullName,
                            "OnEnginePreload event Action");
                    }
                }

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
                    isServerTarget
                        ? "Source mod loading deferred until Terraria LaunchGame setup is complete."
                        : "Source mod loading deferred until Terraria's first engine preload update.");
            }
        }

        public static void OnLaunchGameReady()
        {
            if (_isServerTarget)
            {
                LoadMods();
                return;
            }

            try
            {
                lock (Gate)
                {
                    if (_clientArmed || _loaded)
                        return;

                    if (_enginePreloadEvent == null)
                        throw new InvalidOperationException(
                            "Terraria.Main.OnEnginePreload was not initialized.");

                    _enginePreloadHandler = OnClientEnginePreload;
                    _enginePreloadEvent.AddEventHandler(null, _enginePreloadHandler);
                    _clientArmed = true;
                }

                Log.Info(
                    "Terraria LaunchGame setup complete; source mods armed for Main.OnEnginePreload.");
            }
            catch (Exception ex)
            {
                Log.Error(
                    "Could not arm deferred client source-mod loading." +
                    Environment.NewLine +
                    ex);
            }
        }

        private static void OnClientEnginePreload()
        {
            Action handler;
            EventInfo enginePreloadEvent;

            lock (Gate)
            {
                handler = _enginePreloadHandler;
                enginePreloadEvent = _enginePreloadEvent;
                _enginePreloadHandler = null;
                _clientArmed = false;
            }

            try
            {
                if (handler != null && enginePreloadEvent != null)
                    enginePreloadEvent.RemoveEventHandler(null, handler);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not detach deferred engine-preload hook: " + ex.Message);
            }

            LoadMods();
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
                Log.Info("Terraria mod-safe startup point reached; loading source mods.");

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
                nameof(OnLaunchGameReady),
                Type.EmptyTypes);

            if (_runGameMethod == null)
                throw new InvalidOperationException("Deferred RunGame target was not initialized.");
            if (callback == null)
                throw new MissingMethodException(
                    typeof(DeferredModBootstrap).FullName,
                    nameof(OnLaunchGameReady));

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

                var ready = new CodeInstruction(OpCodes.Call, callback);
                ready.labels.AddRange(instruction.labels);
                instruction.labels.Clear();
                ready.blocks.AddRange(instruction.blocks);
                instruction.blocks.Clear();

                code.Insert(i, ready);
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
