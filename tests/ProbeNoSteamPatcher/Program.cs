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

    var enginePreloadField = mainType.Fields.SingleOrDefault(field =>
        field.Name == "OnEnginePreload" &&
        field.IsStatic &&
        field.FieldType.FullName == "System.Action")
        ?? throw new InvalidOperationException(
            "Terraria.Main.OnEnginePreload backing field was not found.");

    var isEnginePreloadedField = mainType.Fields.SingleOrDefault(field =>
        field.Name == "IsEnginePreloaded" &&
        field.IsStatic &&
        field.FieldType.MetadataType == MetadataType.Boolean)
        ?? throw new InvalidOperationException(
            "Terraria.Main.IsEnginePreloaded was not found.");

    var clientInitialize = mainType.Methods.SingleOrDefault(method =>
        method.Name == "ClientInitialize" &&
        !method.IsStatic &&
        method.Parameters.Count == 0 &&
        method.ReturnType.MetadataType == MetadataType.Void)
        ?? throw new InvalidOperationException(
            "Terraria.Main.ClientInitialize() was not found.");

    var applyChangesCalls = clientInitialize.Body.Instructions
        .Where(instruction =>
            (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
            instruction.Operand is MethodReference method &&
            method.Name == "ApplyChanges" &&
            method.Parameters.Count == 0)
        .ToArray();

    if (applyChangesCalls.Length != 1)
    {
        throw new InvalidOperationException(
            "Expected exactly one graphics ApplyChanges() call in " +
            "Terraria.Main.ClientInitialize, found " + applyChangesCalls.Length + ".");
    }

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

    var socialCall = socialInitializeCalls[0];
    var runGameIl = runGame.Body.GetILProcessor();
    var setServer = Instruction.Create(OpCodes.Ldc_I4_1);
    var storeServer = Instruction.Create(OpCodes.Stsfld, dedServField);
    var setClient = Instruction.Create(OpCodes.Ldc_I4_0);
    var storeClient = Instruction.Create(OpCodes.Stsfld, dedServField);

    runGameIl.InsertBefore(socialCall, setServer);
    runGameIl.InsertBefore(socialCall, storeServer);
    runGameIl.InsertAfter(socialCall, setClient);
    runGameIl.InsertAfter(setClient, storeClient);

    // Bare/headless FNA does not reliably advance to Terraria's first Update(),
    // but it does create the client GraphicsDevice and execute graphics.ApplyChanges().
    // Raise Terraria's real engine-preload event immediately after that exact call in
    // this disposable probe copy only. Production Terraria/gloader are unchanged.
    var actionInvoke = module.ImportReference(
        typeof(Action).GetMethod(nameof(Action.Invoke))
        ?? throw new InvalidOperationException("System.Action.Invoke was not found."));

    var clientInitializeIl = clientInitialize.Body.GetILProcessor();
    var cursor = applyChangesCalls[0];
    var skipPreload = Instruction.Create(OpCodes.Nop);
    var injected = new[]
    {
        Instruction.Create(OpCodes.Ldc_I4_1),
        Instruction.Create(OpCodes.Stsfld, isEnginePreloadedField),
        Instruction.Create(OpCodes.Ldsfld, enginePreloadField),
        Instruction.Create(OpCodes.Brfalse_S, skipPreload),
        Instruction.Create(OpCodes.Ldsfld, enginePreloadField),
        Instruction.Create(OpCodes.Callvirt, actionInvoke),
        skipPreload
    };

    foreach (var instruction in injected)
    {
        clientInitializeIl.InsertAfter(cursor, instruction);
        cursor = instruction;
    }

    assembly.Write(temporaryPath);
}

File.Move(temporaryPath, path, overwrite: true);
Console.WriteLine(
    "Patched probe Terraria.exe for offline SocialAPI and post-graphics engine preload.");
