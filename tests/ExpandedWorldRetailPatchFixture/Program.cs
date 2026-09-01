using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

internal static class Program
{
    private const string HarmonyId = "gloader.expandedworlds.retail-patch-fixture";

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2)
                throw new ArgumentException("Usage: ExpandedWorldRetailPatchFixture <TerrariaServer.exe> <repo-root>");

            string gamePath = Path.GetFullPath(args[0]);
            string repoRoot = Path.GetFullPath(args[1]);
            string gameDirectory = Path.GetDirectoryName(gamePath)
                ?? throw new InvalidOperationException("Could not resolve Terraria directory.");
            string modDirectory = Path.Combine(repoRoot, "gmods", "ExpandedWorlds");

            if (!File.Exists(gamePath))
                throw new FileNotFoundException("Terraria assembly not found.", gamePath);
            if (!Directory.Exists(modDirectory))
                throw new DirectoryNotFoundException(modDirectory);

            AppDomain.CurrentDomain.AssemblyResolve += (_, eventArgs) =>
                ResolveFromDirectory(gameDirectory, eventArgs.Name);

            Assembly gameAssembly = Assembly.LoadFrom(gamePath);
            Console.WriteLine("Loaded game assembly: " + gameAssembly.FullName);

            Assembly modAssembly = CompileExpandedWorlds(gameAssembly, gameDirectory, modDirectory);
            Console.WriteLine("Compiled Expanded Worlds against exact game assembly: " + modAssembly.FullName);

            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll(modAssembly);

            var patched = Harmony.GetAllPatchedMethods()
                .Where(method =>
                {
                    Patches info = Harmony.GetPatchInfo(method);
                    return info != null && info.Owners.Contains(HarmonyId);
                })
                .OrderBy(method => method.DeclaringType?.FullName)
                .ThenBy(method => method.Name)
                .ToList();

            if (patched.Count < 10)
            {
                throw new InvalidOperationException(
                    "Expanded Worlds retail patch fixture expected a substantial patch set; Harmony applied only " +
                    patched.Count + " methods.");
            }

            Console.WriteLine("PASS: Harmony resolved and applied " + patched.Count + " Expanded Worlds patches to exact Terraria 1.4.5.8 IL.");
            foreach (MethodBase method in patched)
                Console.WriteLine("  " + method.DeclaringType?.FullName + "." + method.Name);

            harmony.UnpatchAll(HarmonyId);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: Expanded Worlds retail patch applicability audit failed.");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static Assembly CompileExpandedWorlds(
        Assembly gameAssembly,
        string gameDirectory,
        string modDirectory)
    {
        var parseOptions = new CSharpParseOptions(
            languageVersion: LanguageVersion.Latest,
            documentationMode: DocumentationMode.None,
            kind: SourceCodeKind.Regular,
            preprocessorSymbols: new[] { "GLOADER", "GLOADER_CLIENT" });

        SyntaxTree[] trees = Directory
            .EnumerateFiles(modDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => CSharpSyntaxTree.ParseText(
                File.ReadAllText(path),
                parseOptions,
                path))
            .ToArray();

        IReadOnlyList<MetadataReference> references = CollectReferences(gameAssembly, gameDirectory);
        var compilation = CSharpCompilation.Create(
            "gloader.mod.ExpandedWorlds.retailfixture",
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: true,
                deterministic: true));

        using (var peStream = new MemoryStream())
        {
            var result = compilation.Emit(peStream);
            if (!result.Success)
            {
                string errors = string.Join(
                    Environment.NewLine,
                    result.Diagnostics
                        .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error || diagnostic.IsWarningAsError)
                        .Select(diagnostic => diagnostic.ToString()));
                throw new InvalidOperationException("Expanded Worlds failed to compile against exact Terraria assembly:" + Environment.NewLine + errors);
            }

            return Assembly.Load(peStream.ToArray());
        }
    }

    private static IReadOnlyList<MetadataReference> CollectReferences(
        Assembly gameAssembly,
        string gameDirectory)
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            AddAssembly(paths, assembly, false);

        foreach (string path in Directory.EnumerateFiles(gameDirectory, "*.dll", SearchOption.TopDirectoryOnly))
            AddManagedPath(paths, path, false);
        foreach (string path in Directory.EnumerateFiles(gameDirectory, "*.exe", SearchOption.TopDirectoryOnly))
            AddManagedPath(paths, path, false);

        string gameName = gameAssembly.GetName().Name;
        if (string.Equals(gameName, "TerrariaServer", StringComparison.OrdinalIgnoreCase))
            paths.Remove("Terraria");
        else if (string.Equals(gameName, "Terraria", StringComparison.OrdinalIgnoreCase))
            paths.Remove("TerrariaServer");

        AddAssembly(paths, gameAssembly, true);

        return paths.Values
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static Assembly ResolveFromDirectory(string directory, string requestedName)
    {
        var requested = new AssemblyName(requestedName);
        foreach (string extension in new[] { ".dll", ".exe" })
        {
            string path = Path.Combine(directory, requested.Name + extension);
            if (File.Exists(path))
                return Assembly.LoadFrom(path);
        }
        return null;
    }

    private static void AddAssembly(IDictionary<string, string> paths, Assembly assembly, bool overwrite)
    {
        try
        {
            if (!assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                AddManagedPath(paths, assembly.Location, overwrite);
        }
        catch (NotSupportedException)
        {
        }
    }

    private static void AddManagedPath(IDictionary<string, string> paths, string path, bool overwrite)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            string name = AssemblyName.GetAssemblyName(fullPath).Name;
            if (overwrite || !paths.ContainsKey(name))
                paths[name] = fullPath;
        }
        catch (BadImageFormatException)
        {
        }
        catch (FileLoadException)
        {
        }
        catch (FileNotFoundException)
        {
        }
    }
}
