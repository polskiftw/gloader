using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GLoader.CompilerHost
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                if (args == null || args.Length != 2 || !string.Equals(args[0], "--manifest", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine("Usage: gloader.compiler.exe --manifest <compile-job.txt>");
                    return 64;
                }

                var manifestPath = Path.GetFullPath(args[1]);
                var job = CompileJob.Load(manifestPath);
                Compile(job);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static void Compile(CompileJob job)
        {
            var parseOptions = new CSharpParseOptions(
                languageVersion: LanguageVersion.Latest,
                documentationMode: DocumentationMode.None,
                kind: SourceCodeKind.Regular,
                preprocessorSymbols: job.Symbols);

            var syntaxTrees = job.SourceFiles
                .Select(path => CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path),
                    parseOptions,
                    path))
                .ToArray();

            var references = new List<MetadataReference>();
            foreach (var path in job.ReferenceFiles)
            {
                try
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                }
                catch (BadImageFormatException)
                {
                    // Native binaries are harmless in the reference list; ignore them.
                }
            }

            var compilation = CSharpCompilation.Create(
                job.AssemblyName,
                syntaxTrees,
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    allowUnsafe: true,
                    deterministic: true));

            var outputDirectory = Path.GetDirectoryName(job.OutputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new InvalidOperationException("Compiler output path has no directory.");
            Directory.CreateDirectory(outputDirectory);

            var stagingPath = job.OutputPath + ".new";
            if (File.Exists(stagingPath))
                File.Delete(stagingPath);

            try
            {
                using (var stream = new FileStream(
                    stagingPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read))
                {
                    var result = compilation.Emit(stream);
                    if (!result.Success)
                    {
                        var diagnostics = result.Diagnostics
                            .Where(diagnostic =>
                                diagnostic.Severity == DiagnosticSeverity.Error ||
                                diagnostic.IsWarningAsError)
                            .OrderBy(diagnostic => diagnostic.Location.SourceTree?.FilePath)
                            .ThenBy(diagnostic => diagnostic.Location.GetLineSpan().StartLinePosition.Line)
                            .Select(FormatDiagnostic)
                            .ToArray();

                        throw new InvalidOperationException(
                            diagnostics.Length == 0
                                ? "Compilation failed with no compiler diagnostics."
                                : string.Join(Environment.NewLine, diagnostics));
                    }
                }

                if (File.Exists(job.OutputPath))
                    File.Delete(job.OutputPath);
                File.Move(stagingPath, job.OutputPath);
            }
            finally
            {
                try
                {
                    if (File.Exists(stagingPath))
                        File.Delete(stagingPath);
                }
                catch
                {
                    // Do not hide the real compiler error with cleanup noise.
                }
            }
        }

        private static string FormatDiagnostic(Diagnostic diagnostic)
        {
            var span = diagnostic.Location.GetLineSpan();
            if (!span.IsValid || string.IsNullOrWhiteSpace(span.Path))
                return diagnostic.ToString();

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}({1},{2}): {3} {4}: {5}",
                span.Path,
                span.StartLinePosition.Line + 1,
                span.StartLinePosition.Character + 1,
                diagnostic.Severity.ToString().ToLowerInvariant(),
                diagnostic.Id,
                diagnostic.GetMessage());
        }
    }

    internal sealed class CompileJob
    {
        public string AssemblyName { get; private set; }
        public string OutputPath { get; private set; }
        public List<string> Symbols { get; private set; }
        public List<string> SourceFiles { get; private set; }
        public List<string> ReferenceFiles { get; private set; }

        public static CompileJob Load(string manifestPath)
        {
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException("Compile manifest not found.", manifestPath);

            string assemblyName = null;
            string outputPath = null;
            var symbols = new List<string>();
            var sources = new List<string>();
            var references = new List<string>();
            var versionSeen = false;

            foreach (var rawLine in File.ReadAllLines(manifestPath))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                    continue;

                var equals = rawLine.IndexOf('=');
                if (equals <= 0)
                    throw new InvalidDataException("Malformed compile manifest line: " + rawLine);

                var key = rawLine.Substring(0, equals).Trim();
                var encoded = rawLine.Substring(equals + 1);
                var value = Uri.UnescapeDataString(encoded);

                switch (key.ToLowerInvariant())
                {
                    case "version":
                        if (!string.Equals(value, "1", StringComparison.Ordinal))
                            throw new InvalidDataException("Unsupported compile manifest version: " + value);
                        versionSeen = true;
                        break;
                    case "assembly":
                        assemblyName = value;
                        break;
                    case "output":
                        outputPath = Path.GetFullPath(value);
                        break;
                    case "symbol":
                        if (!string.IsNullOrWhiteSpace(value)) symbols.Add(value);
                        break;
                    case "source":
                        sources.Add(Path.GetFullPath(value));
                        break;
                    case "reference":
                        references.Add(Path.GetFullPath(value));
                        break;
                    default:
                        throw new InvalidDataException("Unknown compile manifest key: " + key);
                }
            }

            if (!versionSeen)
                throw new InvalidDataException("Compile manifest is missing version=1.");
            if (string.IsNullOrWhiteSpace(assemblyName))
                throw new InvalidDataException("Compile manifest is missing assembly name.");
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new InvalidDataException("Compile manifest is missing output path.");
            if (sources.Count == 0)
                throw new InvalidDataException("Compile manifest contains no source files.");

            foreach (var source in sources)
            {
                if (!File.Exists(source))
                    throw new FileNotFoundException("Source file not found.", source);
            }
            foreach (var reference in references)
            {
                if (!File.Exists(reference))
                    throw new FileNotFoundException("Reference file not found.", reference);
            }

            return new CompileJob
            {
                AssemblyName = assemblyName,
                OutputPath = outputPath,
                Symbols = symbols,
                SourceFiles = sources,
                ReferenceFiles = references
            };
        }
    }
}
