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

    var loadContent = mainType.Methods.SingleOrDefault(method =>
        method.Name == "LoadContent" &&
        !method.IsStatic &&
        method.Parameters.Count == 0 &&
        method.ReturnType.MetadataType == MetadataType.Void)
        ?? throw new InvalidOperationException(
            "Terraria.Main.LoadContent() was not found.");

    var loadContentReturns = loadContent.Body.Instructions
        .Where(instruction => instruction.OpCode == OpCodes.Ret)
        .ToArray();

    if (loadContentReturns.Length != 1)
    {
        throw new InvalidOperationException(
            "Expected exactly one return in Terraria.Main.LoadContent(), found " +
            loadContentReturns.Length + ".");
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

    // The headless Xvfb probe reaches Main.LoadContent() but FNA never advances
    // to the first Main.Update() where Terraria normally raises OnEnginePreload.
    // Raise that exact event at the end of LoadContent in the disposable probe
    // copy so gloader is exercised at essentially the same post-graphics point.
    var actionInvoke = module.ImportReference(
        typeof(Action).GetMethod(nameof(Action.Invoke))
        ?? throw new InvalidOperationException("System.Action.Invoke was not found."));

    var loadContentIl = loadContent.Body.GetILProcessor();
    var loadContentReturn = loadContentReturns[0];
    var skipPreload = Instruction.Create(OpCodes.Nop);

    loadContentIl.InsertBefore(loadContentReturn, Instruction.Create(OpCodes.Ldc_I4_1));
    loadContentIl.InsertBefore(
        loadContentReturn,
        Instruction.Create(OpCodes.Stsfld, isEnginePreloadedField));
    loadContentIl.InsertBefore(
        loadContentReturn,
        Instruction.Create(OpCodes.Ldsfld, enginePreloadField));
    loadContentIl.InsertBefore(
        loadContentReturn,
        Instruction.Create(OpCodes.Brfalse_S, skipPreload));
    loadContentIl.InsertBefore(
        loadContentReturn,
        Instruction.Create(OpCodes.Ldsfld, enginePreloadField));
    loadContentIl.InsertBefore(
        loadContentReturn,
        Instruction.Create(OpCodes.Callvirt, actionInvoke));
    loadContentIl.InsertBefore(loadContentReturn, skipPreload);

    assembly.Write(temporaryPath);
}

File.Move(temporaryPath, path, overwrite: true);
Console.WriteLine(
    "Patched probe Terraria.exe for offline SocialAPI and deterministic engine preload.");
