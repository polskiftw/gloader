using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GLoader
{
    internal sealed class AssemblyResolver : IDisposable
    {
        private const string EmbeddedLibraryMarker = ".Libraries.NET.";

        private readonly string _root;
        private readonly string _dependencies;
        private readonly List<string> _extraDirectories = new List<string>();
        private readonly HashSet<string> _active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _embeddedImages =
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new object();

        private Assembly _preferredAssembly;
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
            IndexEmbeddedManagedLibraries(assembly);
        }

        public void AddDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            var fullPath = Path.GetFullPath(path);
            if (!_extraDirectories.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                _extraDirectories.Add(fullPath);
        }

        public IEnumerable<KeyValuePair<string, byte[]>> GetEmbeddedAssemblyImages()
        {
            lock (_gate)
            {
                return _embeddedImages
                    .Select(pair => new KeyValuePair<string, byte[]>(pair.Key, pair.Value))
                    .ToArray();
            }
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
                string.Equals(_preferredAssembly.GetName().Name, requestedName, StringComparison.OrdinalIgnoreCase))
            {
                return _preferredAssembly;
            }

            var loaded = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(assembly =>
                {
                    try
                    {
                        return string.Equals(assembly.GetName().Name, requestedName, StringComparison.OrdinalIgnoreCase);
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
                            var requesterDirectory = Path.GetDirectoryName(args.RequestingAssembly.Location);
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

        private void IndexEmbeddedManagedLibraries(Assembly assembly)
        {
            if (assembly == null)
                return;

            string[] resources;
            try
            {
                resources = assembly.GetManifestResourceNames();
            }
            catch
            {
                return;
            }

            foreach (var resource in resources)
            {
                var marker = resource.IndexOf(
                    EmbeddedLibraryMarker,
                    StringComparison.OrdinalIgnoreCase);

                if (marker < 0 ||
                    !resource.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var start = marker + EmbeddedLibraryMarker.Length;
                var length = resource.Length - start - ".dll".Length;
                if (length <= 0)
                    continue;

                var simpleName = resource.Substring(start, length);

                lock (_gate)
                {
                    if (_embeddedImages.ContainsKey(simpleName))
                        continue;
                }

                try
                {
                    using (var stream = assembly.GetManifestResourceStream(resource))
                    {
                        if (stream == null)
                            continue;

                        using (var memory = new MemoryStream())
                        {
                            stream.CopyTo(memory);
                            var image = memory.ToArray();

                            lock (_gate)
                                _embeddedImages[simpleName] = image;
                        }
                    }

                    Log.Info("Indexed embedded game library: " + simpleName);
                }
                catch (Exception ex)
                {
                    Log.Warn("Could not read embedded game library " + simpleName + ": " + ex.Message);
                }
            }
        }

        private Assembly TryLoadEmbedded(string requestedName)
        {
            byte[] image;
            lock (_gate)
            {
                if (!_embeddedImages.TryGetValue(requestedName, out image))
                    return null;
            }

            try
            {
                var assembly = Assembly.Load(image);
                Log.Info("Loaded embedded game library: " + requestedName);
                return assembly;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not load embedded game library " + requestedName + ": " + ex.Message);
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
