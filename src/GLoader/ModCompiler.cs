using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace GLoader
{
    internal static class ModCompiler
    {
        public static Assembly Compile(
            ModSource mod,
            IReadOnlyList<string> references,
            bool isServerTarget,
            string cacheRoot,
            string compilerPath)
        {
            if (string.IsNullOrWhiteSpace(cacheRoot))
                throw new ArgumentException("A compiled-mod cache directory is required.", nameof(cacheRoot));
            if (string.IsNullOrWhiteSpace(compilerPath))
                throw new ArgumentException("A compiler helper path is required.", nameof(compilerPath));

            compilerPath = Path.GetFullPath(compilerPath);
            if (!File.Exists(compilerPath))
            {
                throw new FileNotFoundException(
                    "gloader's compiler helper is missing. Re-extract the complete release package so gdeps\\compiler is present.",
                    compilerPath);
            }

            var safeId = SanitizeAssemblyName(mod.Id);
            var assemblyName = "gloader.mod." + safeId;
            var targetCache = Path.Combine(
                Path.GetFullPath(cacheRoot),
                isServerTarget ? "server" : "client");
            Directory.CreateDirectory(targetCache);

            var outputPath = Path.Combine(targetCache, safeId + ".dll");
            var manifestPath = Path.Combine(
                targetCache,
                safeId + ".compile-job-" + Guid.NewGuid().ToString("N") + ".txt");

            try
            {
                WriteManifest(
                    manifestPath,
                    assemblyName,
                    outputPath,
                    mod.SourceFiles,
                    references,
                    isServerTarget);

                RunCompiler(compilerPath, manifestPath, mod.DisplayName);

                if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
                {
                    throw new ModCompilationException(
                        mod.DisplayName,
                        "Compiler helper exited successfully but did not produce a usable DLL: " + outputPath);
                }

                // The compiled mod is a normal file-backed assembly. Roslyn itself runs
                // in gloader.compiler.exe and exits before Terraria starts, so the main
                // loader no longer contains an in-process arbitrary-code compiler.
                return Assembly.LoadFrom(outputPath);
            }
            finally
            {
                try
                {
                    if (File.Exists(manifestPath))
                        File.Delete(manifestPath);
                }
                catch
                {
                    // Cleanup must never hide the actual compile/load failure.
                }
            }
        }

        private static void WriteManifest(
            string manifestPath,
            string assemblyName,
            string outputPath,
            IReadOnlyList<string> sourceFiles,
            IReadOnlyList<string> references,
            bool isServerTarget)
        {
            var lines = new List<string>
            {
                "version=" + Escape("1"),
                "assembly=" + Escape(assemblyName),
                "output=" + Escape(Path.GetFullPath(outputPath)),
                "symbol=" + Escape("GLOADER"),
                "symbol=" + Escape(isServerTarget ? "GLOADER_SERVER" : "GLOADER_CLIENT")
            };

            foreach (var source in sourceFiles)
                lines.Add("source=" + Escape(Path.GetFullPath(source)));
            foreach (var reference in references)
                lines.Add("reference=" + Escape(Path.GetFullPath(reference)));

            File.WriteAllLines(manifestPath, lines, new UTF8Encoding(false));
        }

        private static void RunCompiler(string compilerPath, string manifestPath, string modDisplayName)
        {
            var start = new ProcessStartInfo
            {
                FileName = compilerPath,
                Arguments = "--manifest " + Quote(manifestPath),
                WorkingDirectory = Path.GetDirectoryName(compilerPath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = new Process { StartInfo = start })
            {
                if (!process.Start())
                    throw new ModCompilationException(modDisplayName, "Windows could not start gloader.compiler.exe.");

                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                Task.WaitAll(stdoutTask, stderrTask);

                var stdout = stdoutTask.Result == null ? string.Empty : stdoutTask.Result.Trim();
                var stderr = stderrTask.Result == null ? string.Empty : stderrTask.Result.Trim();

                if (process.ExitCode != 0)
                {
                    var details = string.Join(
                        Environment.NewLine,
                        new[] { stderr, stdout }.Where(value => !string.IsNullOrWhiteSpace(value)));
                    if (string.IsNullOrWhiteSpace(details))
                        details = "Compiler helper exited with code " + process.ExitCode + ".";
                    throw new ModCompilationException(modDisplayName, details);
                }
            }
        }

        private static string Escape(string value)
        {
            return Uri.EscapeDataString(value ?? string.Empty);
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private static string SanitizeAssemblyName(string value)
        {
            var characters = value
                .Select(character =>
                    char.IsLetterOrDigit(character) || character == '.' || character == '_'
                        ? character
                        : '_')
                .ToArray();

            return new string(characters);
        }
    }

    internal sealed class ModCompilationException : Exception
    {
        public ModCompilationException(string modName, string diagnostics)
            : base("Mod '" + modName + "' did not compile:" + Environment.NewLine + diagnostics)
        {
        }
    }
}
