using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Threading.Tasks;

namespace GLoader
{
    internal static class GameBootstrap
    {
        public static Assembly Load(string targetPath)
        {
            var fullTargetPath = Path.GetFullPath(targetPath);
            ConfigurePrivateFnaTitleLocation(fullTargetPath);

            Log.Info("Loading managed Terraria assembly into the 64-bit CoreCLR host.");
            var gameAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(fullTargetPath);
            InitializePrivateRuntimeNativeDirectory(gameAssembly, fullTargetPath);
            return gameAssembly;
        }

        public static int InvokeEntryPoint(Assembly gameAssembly, string[] gameArguments)
        {
            var entryPoint = gameAssembly.EntryPoint;
            if (entryPoint == null)
                throw new MissingMethodException("Terraria assembly has no managed entry point.");

            var parameters = entryPoint.GetParameters();
            object[] invokeArguments;

            if (parameters.Length == 0)
            {
                invokeArguments = null;
            }
            else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string[]))
            {
                invokeArguments = new object[] { gameArguments ?? Array.Empty<string>() };
            }
            else
            {
                throw new NotSupportedException(
                    "Unsupported Terraria entry point signature: " + entryPoint);
            }

            try
            {
                var result = entryPoint.Invoke(null, invokeArguments);

                if (result is Task<int> intTask)
                    return intTask.GetAwaiter().GetResult();

                if (result is Task task)
                {
                    task.GetAwaiter().GetResult();
                    return 0;
                }

                return result is int exitCode ? exitCode : 0;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        private static void ConfigurePrivateFnaTitleLocation(string targetPath)
        {
            if (!IsPrivateRuntimeTarget(targetPath))
                return;

            // FNA normally uses AppDomain.CurrentDomain.BaseDirectory on Windows.
            // gloader deliberately keeps its managed host under gdeps, so that would
            // make FNA look for Content under Terraria\gdeps\Content. Force FNA to use
            // SDL_GetBasePath instead, which resolves to the public gloader.exe beside
            // Terraria.exe and therefore the real Terraria\Content directory.
            //
            // The pinned FNA build copies the legacy SDL2 variable over the newer
            // variable during FNAPlatform initialization, so both must be set.
            Environment.SetEnvironmentVariable("FNA_SDL2_FORCE_BASE_PATH", "1");
            Environment.SetEnvironmentVariable("FNA_SDL_FORCE_BASE_PATH", "1");
            Log.Info("FNA title base forced to SDL executable directory for private x64 runtime.");
        }

        private static void InitializePrivateRuntimeNativeDirectory(Assembly gameAssembly, string targetPath)
        {
            if (!IsPrivateRuntimeTarget(targetPath))
                return;

            var monoLaunch = gameAssembly.GetType("Terraria.MonoLaunch", throwOnError: false);
            if (monoLaunch == null)
            {
                Log.Info("Private runtime has no Terraria.MonoLaunch type; native-directory preinitialization skipped.");
                return;
            }

            var runtimeDirectory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(runtimeDirectory))
                throw new InvalidOperationException("Could not determine the private Terraria runtime directory.");

            runtimeDirectory = Path.GetFullPath(runtimeDirectory);
            var originalDirectory = Environment.CurrentDirectory;

            // TerrariaNetCore v1.4.5.8 computes MonoLaunch.NativesDir in its static
            // initializer from Environment.CurrentDirectory. The game itself must keep
            // cwd at the Steam Terraria root for vanilla relative paths, while the
            // rebuilt native libraries live under gdeps\x64-runtime. Initialize only
            // MonoLaunch while cwd points at the private runtime, then restore cwd before
            // any Terraria game code executes. NativesDir remains locked to the correct
            // private runtime path for the lifetime of the process.
            try
            {
                Directory.SetCurrentDirectory(runtimeDirectory);
                RuntimeHelpers.RunClassConstructor(monoLaunch.TypeHandle);
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDirectory);
            }

            var nativesField = monoLaunch.GetField(
                "NativesDir",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (nativesField == null || nativesField.GetValue(null) is not string nativesDirectory ||
                string.IsNullOrWhiteSpace(nativesDirectory))
            {
                throw new InvalidOperationException(
                    "The private Terraria runtime did not expose MonoLaunch.NativesDir after initialization.");
            }

            nativesDirectory = Path.GetFullPath(nativesDirectory);
            var expectedDirectory = Path.GetFullPath(
                Path.Combine(runtimeDirectory, "Libraries", "Native", "Windows"));

            if (!PathsEqual(nativesDirectory, expectedDirectory))
            {
                throw new InvalidOperationException(
                    "TerrariaNetCore native directory initialized to the wrong root. Expected '" +
                    expectedDirectory + "', got '" + nativesDirectory + "'.");
            }

            if (!Directory.Exists(nativesDirectory))
            {
                throw new DirectoryNotFoundException(
                    "The private Terraria runtime is missing its native Windows library directory: " +
                    nativesDirectory);
            }

            Log.Info("TerrariaNetCore native root locked to private runtime: " + nativesDirectory);
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPrivateRuntimeTarget(string targetPath)
        {
            var directory = Path.GetDirectoryName(targetPath);
            while (!string.IsNullOrWhiteSpace(directory))
            {
                if (string.Equals(
                    Path.GetFileName(directory),
                    TargetLocator.X64RuntimeDirectoryName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    var parent = Path.GetDirectoryName(directory);
                    return !string.IsNullOrWhiteSpace(parent) &&
                        string.Equals(
                            Path.GetFileName(parent),
                            "gdeps",
                            StringComparison.OrdinalIgnoreCase);
                }

                var parentDirectory = Path.GetDirectoryName(directory);
                if (string.IsNullOrWhiteSpace(parentDirectory) ||
                    string.Equals(parentDirectory, directory, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                directory = parentDirectory;
            }

            return false;
        }
    }
}
