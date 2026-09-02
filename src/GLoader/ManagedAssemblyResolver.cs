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
                        return MatchesRequestedIdentity(assembly.GetName(), requested);
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
                        if (!MatchesRequestedIdentity(candidateName, requested))
                        {
                            continue;
                        }

                        // Do not use Assembly.LoadFrom here. We are already inside the
                        // AssemblyResolve event precisely because normal probing did not
                        // find this file (the release package keeps dependencies in gdeps).
                        // LoadFrom can re-enter the binder for the same strong-name identity,
                        // which previously caused both a resolver stack overflow and a
                        // false FileNotFound for an exact System.Memory.dll that was present.
                        // Loading the selected bytes binds this request directly; dependent
                        // assemblies still come back through this resolver normally.
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

        private static bool MatchesRequestedIdentity(AssemblyName candidate, AssemblyName requested)
        {
            if (candidate == null || requested == null ||
                !string.Equals(candidate.Name, requested.Name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Weak-named assemblies historically bind by simple name in gloader.
            // Preserve that behavior. Strong-named dependencies stay exact here;
            // package-version roll-forward, if ever needed, should be explicit rather
            // than accidentally selecting a different Terraria/game dependency.
            var requestedToken = requested.GetPublicKeyToken();
            if (requestedToken == null || requestedToken.Length == 0)
            {
                return true;
            }

            var candidateToken = candidate.GetPublicKeyToken();
            if (candidateToken == null || !candidateToken.SequenceEqual(requestedToken))
            {
                return false;
            }

            if (requested.Version != null && candidate.Version != requested.Version)
            {
                return false;
            }

            var requestedCulture = requested.CultureName ?? string.Empty;
            var candidateCulture = candidate.CultureName ?? string.Empty;
            return string.Equals(candidateCulture, requestedCulture, StringComparison.OrdinalIgnoreCase);
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
                            return MatchesRequestedIdentity(assembly.GetName(), requested)
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
