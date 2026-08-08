using System.Diagnostics;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using static Matcher.Globals;

namespace Matcher;

public static class StringDumping {
    private static readonly string[] StringDumpingDependencies = ["System.dll", "Steamworks.NET.dll"];
	private static string DumpingSubdirectory = "dump";

	public static string DumpStrings(string directory, string mvid, string? alias, ModuleDefinition module) {
		var versionName = (alias != null ? alias+"_" : "") + mvid;
		var outputStringsPath = Path.Combine(directory, $"strings_{versionName}.csv");
		var dumpDir = Path.Combine(directory, DumpingSubdirectory);
		var stringDumperPath = Path.Combine(dumpDir, $"dump_{versionName}.exe");

		Directory.CreateDirectory(dumpDir);
		CreateStringDumper(module, outputStringsPath, stringDumperPath);
		EnsureDependenciesPresent(dumpDir);
		RunExe(stringDumperPath);
		return outputStringsPath;
	}

	public static void RunExe(string exe)
        => RunAndWait(Globals.OperatingSystem switch {
			// may or may not work on platforms other than linux lol lmao
            Globals.OS.Windows => $"\"{exe}\"",
            Globals.OS.Linux or Globals.OS.MacOS => $"mono {exe}",
            _ => throw new ArgumentOutOfRangeException()
        });

	public static void RunAndWait(string command) {
        Console.WriteLine($"Running `{command}`...");

        ProcessStartInfo startInfo = Globals.OperatingSystem switch {
            OS.Windows => new ProcessStartInfo {
                WindowStyle = ProcessWindowStyle.Hidden,
                FileName = "cmd.exe",
                Arguments = $"/C \"{command}\""
            },
            OS.Linux => new ProcessStartInfo {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command}\""
            },
            OS.MacOS => new ProcessStartInfo(),
            _ => new ProcessStartInfo()
        };
        startInfo.RedirectStandardOutput = true;
        startInfo.UseShellExecute = false;

        Process process = new() { StartInfo = startInfo };
        process.Start();
        process.WaitForExit();

        Console.WriteLine($"Process exited with code {process.ExitCode}.");
        string output = process.StandardOutput.ReadToEnd();
        if (!string.IsNullOrEmpty(output)) {
            Console.WriteLine("Process output:");
            Console.WriteLine(output);
        }
    }

    public static MethodDefinition FindStringDeobfMethod(ModuleDefinition module) {
        MethodDefinition mainMethod = module.EntryPoint;

        var (type, method) = Matcher.FindStringDeobfMethod(mainMethod);
		return module.GetType(type)?.Methods.Where(m => m.Name == method).Single() ?? throw new Exception($"Couldn't find type {type}");
    }

    public static HashSet<int> FindStringKeys(ModuleDefinition module, out MethodDefinition stringDeobfMethod) {
        Console.WriteLine("Finding string keys...");

        stringDeobfMethod = FindStringDeobfMethod(module);

        // get all the keys this way
        List<Instruction> refs = [];
        foreach (TypeDefinition type in CollectNestedTypes(module.Types)) {
            if (type is null)
                continue;

            foreach (MethodDefinition method in type.Methods) {
                if (method?.Body?.Instructions is not { } instrs)
                    continue;

                foreach (Instruction instr in instrs) {
                    if (instr is null)
                        continue;

                    if (instr.OpCode.Code == Code.Call
                        && instr.Operand is MethodReference operand
                        && (operand.FullName == stringDeobfMethod.FullName)) // TODO awkward hack to avoid resolving the method reference bc we don't have all the dependency binaries present
                        refs.Add(instr);
                }
            }
        }

        HashSet<int> stringKeys = refs.Select(@ref => (int) @ref.Previous!.Operand).ToHashSet();
        Console.WriteLine($"Found {stringKeys.Count} string keys.");
        return stringKeys;
    }

    public static Collection<TypeDefinition> CollectNestedTypes(Collection<TypeDefinition> topLevel) {
        Collection<TypeDefinition> types = [];
        foreach (var type in topLevel)
            VisitTypes(type, types.Add);

        return types;

        static void VisitTypes(TypeDefinition top, Action<TypeDefinition> action) {
            action(top);
            foreach (TypeDefinition type in top.NestedTypes)
                VisitTypes(type, action);
        }
    }

    public static void CreateStringDumper(ModuleDefinition module, string outputStringsPath, string dumperPath) {
        string mvid = module.Mvid.ToString();

        HashSet<int> stringKeys = FindStringKeys(module, out MethodDefinition stringDeobfMethod);

        IMetadataScope mscorlibScope = module.AssemblyReferences.First(asmRef => asmRef.Name == "mscorlib");
        TypeReference stringType = module.TypeSystem.String;
        TypeReference voidType = module.TypeSystem.Void;

        // Manual construction of a bunch of type and method references because Cecil is jank
        MethodReference concat = new("Concat", stringType, stringType);
        for (int i = 0; i < 3; i++) concat.Parameters.Add(new ParameterDefinition(stringType));

        TypeReference streamWriterType = new("System.IO", "StreamWriter", module, mscorlibScope);
        TypeReference textWriterType = new("System.IO", "TextWriter", module, mscorlibScope);

        MethodReference streamWriterConstructor = new(".ctor", voidType, streamWriterType) { HasThis = true };
        streamWriterConstructor.Parameters.Add(new ParameterDefinition(stringType));
        streamWriterConstructor = module.ImportReference(streamWriterConstructor);

        MethodReference textWriterWriteLine = new("WriteLine", voidType, textWriterType) { HasThis = true };
        textWriterWriteLine.Parameters.Add(new ParameterDefinition(stringType));
        textWriterWriteLine = module.ImportReference(textWriterWriteLine);

        MethodReference textWriterDispose = new("Close", voidType, textWriterType) { HasThis = true };
        textWriterDispose = module.ImportReference(textWriterDispose);

        Console.WriteLine("Building string dumper...");
        ILProcessor proc = module.EntryPoint.Body.GetILProcessor();

        proc.Clear();
        proc.Append(proc.Create(OpCodes.Ldstr, outputStringsPath));
        proc.Append(proc.Create(OpCodes.Newobj, streamWriterConstructor));

        foreach (int key in stringKeys) {
            proc.Append(proc.Create(OpCodes.Dup));
            proc.Append(proc.Create(OpCodes.Ldstr, key.ToString()));
            proc.Append(proc.Create(OpCodes.Ldstr, "~,~"));
            proc.Append(proc.Create(OpCodes.Ldc_I4, key));
            proc.Append(proc.Create(OpCodes.Call, stringDeobfMethod));
            proc.Append(proc.Create(OpCodes.Call, concat));
            proc.Append(proc.Create(OpCodes.Callvirt, textWriterWriteLine));
        }

        proc.Append(proc.Create(OpCodes.Callvirt, textWriterDispose));
        proc.Append(proc.Create(OpCodes.Ret));

        module.Write(dumperPath);
        Console.WriteLine($"String dumper written to {dumperPath}.");
    }

    public static void EnsureDependenciesPresent(string directory) {
        foreach (string dependency in StringDumpingDependencies) {
            string destination = Path.Combine(directory, dependency);
            if (!File.Exists(destination))
				throw new Exception($"Missing {dependency} in {directory}, go put it there"); // TODO put the deps there automatically
                // File.Copy(dependency, destination);
        }
    }
}
