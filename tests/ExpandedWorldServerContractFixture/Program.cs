using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

internal static class Program
{
    private sealed record MethodContract(
        string TypeName,
        string MethodName,
        int[]? AllowedParameterCounts = null,
        bool? MustBeStatic = null,
        bool RequireMethodBody = true);

    private static readonly MethodContract[] SharedMethodContracts =
    {
        new("Terraria.WorldGen", "GetWorldSize", new[] { 0 }, true),
        new("Terraria.WorldGen", "SetWorldSize", null, true),
        new("Terraria.WorldGen", "CreateNewWorld", null, true),
        new("Terraria.WorldGen", "clearWorld", null, true),
        new("Terraria.WorldGen", "GenerateWorld", null, true),
        new("Terraria.WorldGen", "GrowGlowTulips", new[] { 0 }, true),
        new("Terraria.WorldGen", "placeTrap", null, true),
        new("Terraria.WorldGen", "AddSpikeCaves", null, true),
        new("Terraria.WorldGen", "PlaceChilletEggs", new[] { 0 }, true),
        new("Terraria.WorldGen", "neonMossBiome", new[] { 3 }, true),
        new("Terraria.WorldGen", "ShroomPatch", new[] { 2 }, true),
        new("Terraria.WorldGen", "PlantAlch", new[] { 0 }, true),
        new("Terraria.WorldGen", "makeTemple", new[] { 2, 3 }, true),
        new("Terraria.GameContent.Biomes.HiveBiome", "CreateHiveTunnel", new[] { 3 }, true),
        new("Terraria.GameContent.Biomes.Desert.DesertDescription", "CreateFromPlacement"),
        new("Terraria.GameContent.Biomes.JunglePass", "ApplyPass", null, false),
        new("Terraria.GameContent.Biomes.JunglePass", "ApplyRandomMovement", null, false),
        new("Terraria.GameContent.Biomes.JunglePass", "PlaceGemsAt", null, false),
        new("Terraria.GameContent.Biomes.JunglePass", "GenerateFinishingTouches", null, false),
    };

    private static readonly string[] SharedTypeContracts =
    {
        "Terraria.WorldGen",
        "Terraria.WorldBuilding.GenVars",
        "Terraria.GameContent.Generation.Dungeon.DungeonData",
        "Terraria.GameContent.Biomes.HiveBiome",
        "Terraria.GameContent.Biomes.Desert.DesertDescription",
        "Terraria.GameContent.Biomes.JunglePass",
    };

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1)
                throw new ArgumentException("Usage: ExpandedWorldServerContractFixture <TerrariaServer.exe>");

            string serverPath = Path.GetFullPath(args[0]);
            if (!File.Exists(serverPath))
                throw new FileNotFoundException("TerrariaServer executable not found.", serverPath);

            using var stream = File.OpenRead(serverPath);
            using var peReader = new PEReader(stream, PEStreamOptions.PrefetchMetadata);

            if (!peReader.HasMetadata || peReader.PEHeaders.CorHeader is null)
            {
                throw new BadImageFormatException(
                    "TerrariaServer.exe does not expose CLI metadata; the official server packaging changed.");
            }

            MetadataReader metadata = peReader.GetMetadataReader();
            Console.WriteLine("TerrariaServer PE machine: " + peReader.PEHeaders.CoffHeader.Machine);
            Console.WriteLine("TerrariaServer CLI flags: " + peReader.PEHeaders.CorHeader.Flags);
            Console.WriteLine("Metadata version: " + metadata.MetadataVersion);

            if (metadata.IsAssembly)
            {
                AssemblyDefinition assembly = metadata.GetAssemblyDefinition();
                Console.WriteLine(
                    "Managed assembly: " + metadata.GetString(assembly.Name) +
                    " " + assembly.Version);
            }

            var types = new Dictionary<string, TypeDefinitionHandle>(StringComparer.Ordinal);
            foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
            {
                TypeDefinition type = metadata.GetTypeDefinition(handle);
                if (!type.GetDeclaringType().IsNil)
                    continue;

                string fullName = FullTypeName(metadata, type);
                if (!types.TryAdd(fullName, handle))
                    throw new InvalidOperationException("Duplicate top-level metadata type: " + fullName);
            }

            foreach (string typeName in SharedTypeContracts)
            {
                if (!types.ContainsKey(typeName))
                    throw new TypeLoadException("Missing shared Terraria type: " + typeName);

                Console.WriteLine("PASS type: " + typeName);
            }

            foreach (MethodContract contract in SharedMethodContracts)
                VerifyMethod(metadata, types, contract);

            Console.WriteLine();
            Console.WriteLine("PASS: official Terraria 1.4.5.8 dedicated-server shared metadata contracts are present.");
            Console.WriteLine(
                "SCOPE: this proves only server-shared type/method/managed-body contracts. " +
                "It does not compile Expanded Worlds against Terraria.exe, apply client Harmony patches, " +
                "or validate New World UI hooks.");
            Console.WriteLine(
                "CLIENT PENDING: UIWorldCreation hooks, client-only Main/API shape, full Harmony applicability, " +
                "generation/save/reload, world-list presentation, map data, and Host & Play require the exact retail client.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: Expanded Worlds official-server shared contract audit failed.");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void VerifyMethod(
        MetadataReader metadata,
        IReadOnlyDictionary<string, TypeDefinitionHandle> types,
        MethodContract contract)
    {
        TypeDefinition type = metadata.GetTypeDefinition(types[contract.TypeName]);

        var candidates = type.GetMethods()
            .Select(handle => metadata.GetMethodDefinition(handle))
            .Where(method => string.Equals(
                metadata.GetString(method.Name),
                contract.MethodName,
                StringComparison.Ordinal))
            .Where(method => contract.AllowedParameterCounts is null ||
                contract.AllowedParameterCounts.Contains(ParameterCount(metadata, method)))
            .Where(method => contract.MustBeStatic is null ||
                method.Attributes.HasFlag(MethodAttributes.Static) == contract.MustBeStatic.Value)
            .ToList();

        if (candidates.Count != 1)
        {
            string parameterRule = contract.AllowedParameterCounts is null
                ? "any audited parameter count"
                : string.Join(" or ", contract.AllowedParameterCounts);
            string staticRule = contract.MustBeStatic is null
                ? "any static/instance shape"
                : contract.MustBeStatic.Value ? "static" : "instance";

            throw new MissingMethodException(
                contract.TypeName,
                contract.MethodName +
                " (expected exactly one " + staticRule + " method with " + parameterRule +
                " parameters; found " + candidates.Count + ")");
        }

        MethodDefinition method = candidates[0];
        if (contract.RequireMethodBody && method.RelativeVirtualAddress == 0)
        {
            throw new InvalidOperationException(
                contract.TypeName + "." + contract.MethodName +
                " has no managed method body in the official server image.");
        }

        Console.WriteLine(
            "PASS method: " + contract.TypeName + "." + contract.MethodName +
            " (params=" + ParameterCount(metadata, method) +
            ", " + (method.Attributes.HasFlag(MethodAttributes.Static) ? "static" : "instance") +
            ", rva=0x" + method.RelativeVirtualAddress.ToString("X") + ")");
    }

    private static int ParameterCount(MetadataReader metadata, MethodDefinition method)
    {
        return method.GetParameters()
            .Select(metadata.GetParameter)
            .Count(parameter => parameter.SequenceNumber > 0);
    }

    private static string FullTypeName(MetadataReader metadata, TypeDefinition type)
    {
        string name = metadata.GetString(type.Name);
        string ns = metadata.GetString(type.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }
}
