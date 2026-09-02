using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GLoader
{
    internal sealed class ManagedAssemblyResolver : IDisposable
    {
        private readonly string[] _directories;
        private readonly Assembly _resourceAssembly;

        // AssemblyResolve may be serviced by more than one ManagedAssemblyResolver
        // instance at once (the bootstrap resolver and the Terraria/resource resolver).
        // Loading a dependency can itself raise AssemblyResolve, so a same-identity
        // request must not be allowed to ping-pong between handlers until the stack dies.
        [ThreadStatic]
        private static HashSet<string> _resolvingIdentities;

        public ManagedAssemblyResolver(params string[] directories)
            : this(null, directories)
        {
        }

        public ManagedAssemblyResolver(Assembly resourceAssembly, params string[] directories)
        {
            _resourceAssembly = resourceAssembly;
            _directories = (directories ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                // The packaged support set is authoritative for compiler/runtime
                // dependencies. Prefer gdeps ahead of Terraria's own directory so an
                // older game-shipped strong-name assembly cannot win by path order.
                .OrderBy(path => IsSupportDirectory(path) ? 0 : 1)
                .ToArray();

            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        public void Dispose()
        {
            AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
        }

        private Assembly Resolve(object sender, ResolveEventArgs args)
        {
            var requestedIdentity = args == null ? null : args.Name;
            if (string.IsNullOrWhiteSpace(requestedIdentity))
            {
                return null;
            }

            var resolving = _resolvingIdentities;
            if (resolving == null)
            {
                resolving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _resolvingIdentities = resolving;
            }

            if (!resolving.Add(requestedIdentity))
            {
                return null;
            }

            try
            {
                return ResolveCore(new AssemblyName(requestedIdentity));
            }
            finally
            {
                resolving.Remove(requestedIdentity);
                if (resolving.Count == 0)
                {
                    _resolvingIdentities = null;
                }
            }
        }

        private Assembly ResolveCore(AssemblyName requested)
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly =>
                {
                    try
                    {
                        return IsCompatibleIdentity(assembly.GetName(), requested);
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

            foreach (var directory in _directories)
            {
                foreach (var extension in new[] { ".dll", ".exe" })
                {
                    var candidate = Path.Combine(directory, requested.Name + extension);
                    if (!File.Exists(candidate))
                    {
                        continue;
                    }

                    try
                    {
                        var candidateName = AssemblyName.GetAssemblyName(candidate);
                        if (!IsCompatibleIdentity(candidateName, requested))
                        {
                            continue;
                        }

                        // Do not use Assembly.LoadFrom here. We are already inside the
                        // AssemblyResolve event precisely because normal probing did not
                        // find this file (the release package keeps dependencies in gdeps).
                        // LoadFrom can re-enter the binder for the same strong-name identity.
                        return Assembly.Load(File.ReadAllBytes(candidate));
                    }
                    catch (BadImageFormatException)
                    {
                        // Native library; not a managed assembly candidate.
                    }
                    catch (FileLoadException)
                    {
                        // Continue searching other locations.
                    }
                }
            }

            return ResolveEmbedded(requested);
        }

        private static bool IsCompatibleIdentity(AssemblyName candidate, AssemblyName requested)
        {
            if (candidate == null || requested == null ||
                !string.Equals(candidate.Name, requested.Name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Weak-named assemblies historically bind by simple name in gloader.
            // Preserve that behavior.
            var requestedToken = requested.GetPublicKeyToken();
            if (requestedToken == null || requestedToken.Length == 0)
            {
                return true;
            }

            var candidateToken = candidate.GetPublicKeyToken();
            if (!PublicKeyTokensEqual(candidateToken, requestedToken))
            {
                return false;
            }

            var requestedCulture = requested.CultureName ?? string.Empty;
            var candidateCulture = candidate.CultureName ?? string.Empty;
            if (!string.Equals(candidateCulture, requestedCulture, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (requested.Version == null || candidate.Version == null)
            {
                return true;
            }

            // NuGet servicing releases can intentionally ship a strong-named assembly
            // whose revision is newer than the compile-time reference. Example: Roslyn
            // 5.9 requests System.Collections.Immutable 10.0.0.0 while the packaged
            // System.Collections.Immutable 10.0.1 asset has assembly version 10.0.0.1.
            // Exact version equality rejects a compatible package and breaks startup.
            // Allow forward servicing within the same major/minor line, never backward
            // or across a major/minor boundary where API compatibility is not implied.
            if (candidate.Version.Major != requested.Version.Major ||
                candidate.Version.Minor != requested.Version.Minor)
            {
                return false;
            }

            return candidate.Version.CompareTo(requested.Version) >= 0;
        }

        private static bool PublicKeyTokensEqual(byte[] candidate, byte[] requested)
        {
            if (candidate == null || requested == null || candidate.Length != requested.Length)
            {
                return false;
            }

            for (var i = 0; i < candidate.Length; i++)
            {
                if (candidate[i] != requested[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSupportDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(
                Path.GetFileName(trimmed),
                "gdeps",
                StringComparison.OrdinalIgnoreCase);
        }

        private Assembly ResolveEmbedded(AssemblyName requested)
        {
            if (_resourceAssembly == null || string.IsNullOrWhiteSpace(requested.Name))
            {
                return null;
            }

            string[] resourceNames;
            try
            {
                resourceNames = _resourceAssembly.GetManifestResourceNames();
            }
            catch
            {
                return null;
            }

            foreach (var extension in new[] { ".dll", ".exe" })
            {
                var fileName = requested.Name + extension;
                var suffix = "." + fileName;
                var resourceName = resourceNames.FirstOrDefault(name =>
                    string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

                if (resourceName == null)
                {
                    continue;
                }

                try
                {
                    using (var stream = _resourceAssembly.GetManifestResourceStream(resourceName))
                    {
                        if (stream == null)
                        {
                            continue;
                        }

                        using (var memory = new MemoryStream())
                        {
                            stream.CopyTo(memory);
                            if (memory.Length == 0)
                            {
                                continue;
                            }

                            var assembly = Assembly.Load(memory.ToArray());
                            return IsCompatibleIdentity(assembly.GetName(), requested)
                                ? assembly
                                : null;
                        }
                    }
                }
                catch (BadImageFormatException)
                {
                    // Embedded native library; not a managed assembly candidate.
                }
                catch (FileLoadException)
                {
                    // Continue searching other embedded candidates.
                }
            }

            return null;
        }
    }
}
