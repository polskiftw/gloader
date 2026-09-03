using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace GLoader
{
    internal static class GameBootstrap
    {
        private static readonly object EmbeddedResolverSync = new object();
        private static Assembly _embeddedResourceOwner;
        private static ResolveEventHandler _embeddedAssemblyResolver;

        public static Assembly Load(string targetPath)
        {
            Log.Info("Loading managed Terraria assembly.");
            var gameAssembly = Assembly.LoadFrom(targetPath);

            // Terraria's WindowsLaunch.Main installs an AssemblyResolve handler before
            // Program.LaunchGame so embedded dependencies such as ReLogic are available.
            // gloader intentionally loads and patches mods before invoking that entry point,
            // so Harmony may need those same dependencies earlier while decoding Terraria IL.
            // Mirror Terraria's own resource-backed resolver here, in memory only; do not
            // extract or copy embedded assemblies to disk.
            InstallTerrariaEmbeddedAssemblyResolver(gameAssembly);

            return gameAssembly;
        }

        private static void InstallTerrariaEmbeddedAssemblyResolver(Assembly gameAssembly)
        {
            if (gameAssembly == null)
            {
                throw new ArgumentNullException(nameof(gameAssembly));
            }

            lock (EmbeddedResolverSync)
            {
                if (_embeddedAssemblyResolver != null)
                {
                    return;
                }

                _embeddedResourceOwner = gameAssembly;
                _embeddedAssemblyResolver = ResolveTerrariaEmbeddedAssembly;
                AppDomain.CurrentDomain.AssemblyResolve += _embeddedAssemblyResolver;

                var embeddedDlls = gameAssembly
                    .GetManifestResourceNames()
                    .Count(name => name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

                Log.Info(
                    "Installed Terraria embedded assembly resolver for pre-entrypoint mod patching (" +
                    embeddedDlls + " managed resource candidate(s)).");
            }
        }

        private static Assembly ResolveTerrariaEmbeddedAssembly(object sender, ResolveEventArgs args)
        {
            var owner = _embeddedResourceOwner;
            if (owner == null || args == null || string.IsNullOrWhiteSpace(args.Name))
            {
                return null;
            }

            AssemblyName requested;
            try
            {
                requested = new AssemblyName(args.Name);
            }
            catch
            {
                return null;
            }

            var simpleName = requested.Name;
            if (string.IsNullOrWhiteSpace(simpleName))
            {
                return null;
            }

            // Prefer an already-loaded assembly before reading another embedded copy.
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly =>
                {
                    try
                    {
                        return string.Equals(
                            assembly.GetName().Name,
                            simpleName,
                            StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                });
            if (loaded != null)
            {
                return loaded;
            }

            // Match Terraria 1.4.5.8 WindowsLaunch.Main exactly in spirit: resolve a
            // requested Foo assembly from the manifest resource whose name ends Foo.dll.
            var resourceSuffix = simpleName + ".dll";
            var resourceName = Array.Find(
                owner.GetManifestResourceNames(),
                name => name.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase));
            if (resourceName == null)
            {
                return null;
            }

            using (var stream = owner.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    return null;
                }
                if (stream.Length <= 0 || stream.Length > int.MaxValue)
                {
                    throw new InvalidDataException(
                        "Terraria embedded assembly resource has an invalid length: " + resourceName);
                }

                var bytes = new byte[(int)stream.Length];
                var offset = 0;
                while (offset < bytes.Length)
                {
                    var read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0)
                    {
                        throw new EndOfStreamException(
                            "Terraria embedded assembly resource ended early: " + resourceName);
                    }
                    offset += read;
                }

                var resolved = Assembly.Load(bytes);
                Log.Info(
                    "Loaded Terraria embedded dependency before entry point: " +
                    resolved.GetName().Name);
                return resolved;
            }
        }

        public static int InvokeEntryPoint(Assembly gameAssembly, string[] gameArguments)
        {
            var entryPoint = gameAssembly.EntryPoint;
            if (entryPoint == null)
            {
                throw new MissingMethodException(gameAssembly.FullName, "<entry point>");
            }

            var parameters = entryPoint.GetParameters();
            object[] invokeArguments;

            if (parameters.Length == 0)
            {
                invokeArguments = null;
            }
            else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string[]))
            {
                invokeArguments = new object[] { gameArguments };
            }
            else
            {
                throw new NotSupportedException(
                    "Unsupported Terraria entry point signature: " + entryPoint);
            }

            try
            {
                var result = entryPoint.Invoke(null, invokeArguments);
                return result is int exitCode ? exitCode : 0;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
    }
}
