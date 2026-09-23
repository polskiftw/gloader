using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GLoader
{
    internal sealed class AssemblyResolver : IDisposable
    {
        private readonly string _root;
        private readonly string _dependencies;
        private readonly List<string> _extraDirectories = new List<string>();
        private readonly HashSet<string> _active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _embeddedImages =
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new object();

        private Assembly _preferredAssembly;
        private Assembly _embeddedContainer;
        private string[] _embeddedResourceNames = Array.Empty<string>();
        private bool _installed;

        public AssemblyResolver(string root, string dependencies)
        {
            _root = Path.GetFullPath(root);
            _dependencies = Path.GetFullPath(dependencies);
        }

        public void Install()
        {
            if (_installed)
                return;

            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            _installed = true;
        }

        public void PreferAssembly(Assembly assembly)
        {
            _preferredAssembly = assembly;
        }

        public void IndexEmbeddedLibraries(Assembly container)
        {
            if (container == null)
                return;

            try
            {
                var resourceNames = container
                    .GetManifestResourceNames()
                    .Where(name => name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();

                lock (_gate)
                {
                    _embeddedContainer = container;
                    _embeddedResourceNames = resourceNames;
                    _embeddedImages.Clear();
                }

                if (resourceNames.Length > 0)
                {
                    Log.Info(
                        "Indexed " + resourceNames.Length +
                        " Terraria embedded managed librar" +
                        (resourceNames.Length == 1 ? "y." : "ies."));
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not enumerate Terraria embedded libraries: " + ex.Message);
            }
        }

        public IEnumerable<KeyValuePair<string, byte[]>> GetEmbeddedAssemblyImages()
        {
            string[] names;
            lock (_gate)
                names = _embeddedResourceNames.ToArray();

            var result = new List<KeyValuePair<string, byte[]>>();
            foreach (var resourceName in names)
            {
                var image = ReadEmbeddedResource(resourceName);
                if (image != null)
                    result.Add(new KeyValuePair<string, byte[]>(resourceName, image));
            }

            return result;
        }

        public void AddDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            var fullPath = Path.GetFullPath(path);
            if (!_extraDirectories.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                _extraDirectories.Add(fullPath);
        }

        public void Dispose()
        {
            if (!_installed)
                return;

            AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
            _installed = false;
        }

        private Assembly Resolve(object sender, ResolveEventArgs args)
        {
            AssemblyName requested;
            try
            {
                requested = new AssemblyName(args.Name);
            }
            catch
            {
                return null;
            }

            var requestedName = requested.Name;
            if (string.IsNullOrWhiteSpace(requestedName))
                return null;

            if (_preferredAssembly != null &&
                string.Equals(
                    _preferredAssembly.GetName().Name,
                    requestedName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return _preferredAssembly;
            }

            var loaded = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(assembly =>
                {
                    try
                    {
                        return string.Equals(
                            assembly.GetName().Name,
                            requestedName,
                            StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                });

            if (loaded != null)
                return loaded;

            lock (_gate)
            {
                if (!_active.Add(requestedName))
                    return null;
            }

            try
            {
                var embedded = TryLoadEmbedded(requestedName);
                if (embedded != null)
                    return embedded;

                var directories = new List<string>();

                if (args.RequestingAssembly != null)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(args.RequestingAssembly.Location))
                        {
                            var requesterDirectory =
                                Path.GetDirectoryName(args.RequestingAssembly.Location);

                            if (!string.IsNullOrWhiteSpace(requesterDirectory))
                                directories.Add(requesterDirectory);
                        }
                    }
                    catch
                    {
                    }
                }

                directories.AddRange(_extraDirectories);
                directories.Add(_root);
                directories.Add(_dependencies);

                foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var resolved = TryLoad(directory, requestedName + ".dll")
                        ?? TryLoad(directory, requestedName + ".exe");

                    if (resolved != null)
                        return resolved;
                }

                return null;
            }
            finally
            {
                lock (_gate)
                    _active.Remove(requestedName);
            }
        }

        private Assembly TryLoadEmbedded(string requestedName)
        {
            string resourceName;
            lock (_gate)
            {
                var suffix = requestedName + ".dll";
                resourceName = _embeddedResourceNames.FirstOrDefault(
                    name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            }

            if (string.IsNullOrEmpty(resourceName))
                return null;

            var image = ReadEmbeddedResource(resourceName);
            if (image == null)
                return null;

            try
            {
                var assembly = Assembly.Load(image);
                Log.Info("Loaded Terraria embedded library: " + assembly.GetName().Name);
                return assembly;
            }
            catch (Exception ex)
            {
                Log.Warn(
                    "Could not load Terraria embedded library " +
                    resourceName + ": " + ex.Message);
                return null;
            }
        }

        private byte[] ReadEmbeddedResource(string resourceName)
        {
            Assembly container;
            byte[] cached;

            lock (_gate)
            {
                if (_embeddedImages.TryGetValue(resourceName, out cached))
                    return cached;

                container = _embeddedContainer;
            }

            if (container == null)
                return null;

            try
            {
                using (var stream = container.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        return null;

                    using (var memory = new MemoryStream())
                    {
                        stream.CopyTo(memory);
                        var image = memory.ToArray();

                        lock (_gate)
                            _embeddedImages[resourceName] = image;

                        return image;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn(
                    "Could not read Terraria embedded library " +
                    resourceName + ": " + ex.Message);
                return null;
            }
        }

        private static Assembly TryLoad(string directory, string fileName)
        {
            try
            {
                var path = Path.Combine(directory, fileName);
                if (!File.Exists(path))
                    return null;

                return Assembly.LoadFrom(path);
            }
            catch
            {
                return null;
            }
        }
    }
}
