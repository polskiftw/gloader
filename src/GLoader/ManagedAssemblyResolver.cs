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

        // AssemblyResolve may be serviced by more than one ManagedAssemblyResolver
        // instance at once (the bootstrap resolver and the Terraria/runtime resolver).
        // Loading a dependency can itself raise AssemblyResolve, so a same-identity
        // request must not be allowed to ping-pong between handlers until the stack dies.
        [ThreadStatic]
        private static HashSet<string> _resolvingIdentities;

        public ManagedAssemblyResolver(params string[] directories)
        {
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

                        // Every dependency must already exist as a normal file. Do not
                        // unpack embedded managed payloads into AppData or synthesize a
                        // second on-disk copy; those behaviors are unnecessary for the
                        // shipped package and look exactly like dropper behavior to AV ML.
                        return Assembly.LoadFile(Path.GetFullPath(candidate));
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

            // Returning null lets the CLR and any resolver registered by Terraria try
            // their normal probing behavior. gloader itself never extracts assemblies.
            return null;
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
            // whose revision is newer than the compile-time reference. Allow forward
            // servicing within the same major/minor line, never backward or across a
            // major/minor boundary where API compatibility is not implied.
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
    }
}
