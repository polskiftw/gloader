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

        public ManagedAssemblyResolver(params string[] directories)
            : this(null, directories)
        {
        }

        public ManagedAssemblyResolver(Assembly resourceAssembly, params string[] directories)
        {
            _resourceAssembly = resourceAssembly;
            _directories = new[] { AppDomain.CurrentDomain.BaseDirectory }
                .Concat(directories ?? Array.Empty<string>())
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
            var requested = new AssemblyName(args.Name);

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

                        return Assembly.LoadFrom(candidate);
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
            // Preserve that behavior. Strong-named dependencies, however, must
            // not be substituted across versions: Roslyn 5.9, for example,
            // requires System.Collections.Immutable 10.x and will fail if an
            // older strong-named copy from Terraria or another dependency wins.
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
