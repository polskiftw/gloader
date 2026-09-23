using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GLoader
{
    internal sealed class AssemblyResolver : IDisposable
    {
        private const string EmbeddedPrefix = "Terraria.Libraries.";

        private readonly string _root;
        private readonly string _dependencies;
        private readonly List<string> _extraDirectories = new List<string>();
        private readonly HashSet<string> _active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _embeddedResources =
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
        }

        public void IndexEmbeddedLibraries(Assembly container)
        {
            if (container == null)
                return;

            string[] resources;
            try
            {
                resources = container.GetManifestResourceNames();
            }
            catch (Exception ex)
            {
                Log.Warn("Could not enumerate Terraria embedded libraries: " + ex.Message);
                return;
            }

            var indexed = 0;
            foreach (var resourceName in resources
                .Where(name =>
                    name.StartsWith(EmbeddedPrefix, StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                lock (_gate)
                {
                    if (_embeddedResources.ContainsKey(resourceName))
                        continue;
                }

                try
                {
                    using (var stream = container.GetManifestResourceStream(resourceName))
                    {
                        if (stream == null)
                            continue;

                        using (var memory = new MemoryStream())
                        {
                            stream.CopyTo(memory);
                            lock (_gate)
                                _embeddedResources[resourceName] = memory.ToArray();
                        }
                    }

                    indexed++;
                }
                catch (Exception ex)
                {
                    Log.Warn("Could not index embedded library " + resourceName + ": " + ex.Message);
                }
            }

            if (indexed > 0)
                Log.Info("Indexed " + indexed + " Terraria embedded managed librar" + (indexed == 1 ? "y." : "ies."));
        }

        public IEnumerable<KeyValuePair<string, byte[]>> GetEmbeddedAssemblyImages()
        {
            lock (_gate)
            {
                return _embeddedResources
                    .Select(pair => new KeyValuePair<string, byte[]>(pair.Key, pair.Value))
                    .ToArray();
            }
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

        private Assembly TryLoadEmbedded(string requestedName)
        {
            KeyValuePair<string, byte[]> match;
            lock (_gate)
            {
                var suffix = "." + requestedName + ".dll";
                match = _embeddedResources
                    .FirstOrDefault(pair =>
                        pair.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            }

            if (string.IsNullOrEmpty(match.Key) || match.Value == null)
                return null;

            try
            {
                var assembly = Assembly.Load(match.Value);
                Log.Info("Loaded Terraria embedded library: " + assembly.GetName().Name);
                return assembly;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not load Terraria embedded library " + match.Key + ": " + ex.Message);
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
