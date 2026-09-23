using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 1)
    throw new ArgumentException("Usage: ProbeNoSteamPatcher <Terraria.exe>");

var path = Path.GetFullPath(args[0]);
var temporaryPath = path + ".gloader-probe";
var gameDirectory = Path.GetDirectoryName(path)
    ?? throw new InvalidOperationException("Terraria.exe has no parent directory.");

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(gameDirectory);

using (resolver)
using (var assembly = AssemblyDefinition.ReadAssembly(
    path,
    new ReaderParameters
    {
        InMemory = true,
        ReadSymbols = false,
        AssemblyResolver = resolver
    }))
{
    var module = assembly.MainModule;
    var mainType = module.GetType("Terraria.Main")
        ?? throw new InvalidOperationException("Terraria.Main was not found.");
    var programType = module.GetType("Terraria.Program")
        ?? throw new InvalidOperationException("Terraria.Program was not found.");

    var dedServField = mainType.Fields.SingleOrDefault(field =>
        field.Name == "dedServ" &&
        field.IsStatic &&
        field.FieldType.MetadataType == MetadataType.Boolean)
        ?? throw new InvalidOperationException("Terraria.Main.dedServ was not found.");

    var runGame = programType.Methods.SingleOrDefault(method =>
        method.Name == "RunGame" &&
        method.IsStatic &&
        method.Parameters.Count == 0)
        ?? throw new InvalidOperationException("Terraria.Program.RunGame() was not found.");

    var socialInitializeCalls = runGame.Body.Instructions
        .Where(instruction =>
            (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
            instruction.Operand is MethodReference method &&
            method.DeclaringType.FullName == "Terraria.Social.SocialAPI" &&
            method.Name == "Initialize")
        .ToArray();

    if (socialInitializeCalls.Length != 1)
    {
        throw new InvalidOperationException(
            "Expected exactly one SocialAPI.Initialize call in Program.RunGame, found " +
            socialInitializeCalls.Length + ".");
    }

    var call = socialInitializeCalls[0];
    var il = runGame.Body.GetILProcessor();
    var setServer = Instruction.Create(OpCodes.Ldc_I4_1);
    var storeServer = Instruction.Create(OpCodes.Stsfld, dedServField);
    var setClient = Instruction.Create(OpCodes.Ldc_I4_0);
    var storeClient = Instruction.Create(OpCodes.Stsfld, dedServField);

    il.InsertBefore(call, setServer);
    il.InsertBefore(call, storeServer);
    il.InsertAfter(call, setClient);
    il.InsertAfter(setClient, storeClient);

    assembly.Write(temporaryPath);
}

File.Move(temporaryPath, path, overwrite: true);
Console.WriteLine("Patched probe Terraria.exe to bypass Steam SocialAPI only.");
