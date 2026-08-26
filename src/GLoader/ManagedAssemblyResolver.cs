using System;
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
            _directories = directories
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
                        return string.Equals(
                            assembly.GetName().Name,
                            requested.Name,
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

                            return Assembly.Load(memory.ToArray());
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
