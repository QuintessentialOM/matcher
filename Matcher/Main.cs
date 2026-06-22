global using System;
global using System.Collections.Generic;
global using Mono.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Matcher.Matching;
using Matcher.Matching.Classifier;
using Mono.Cecil;

namespace Matcher;

public static class MatcherMain {
	private static readonly Regex MvidPattern = new("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}");

	private static readonly string RunDirectory = "run";
	private static readonly string ExesDirectory = "run/exes";
	private static readonly string StringsDirectory = "run/strings";
	private static readonly string MappingsDirectory = "run/mappings";

	private static string FindFileByMvidOrAlias(string directory, string? mvid, string? alias, string fileExtension) {
		if (mvid != null) {
			var byMvid = Directory.GetFiles(directory, $"*{mvid}*.{fileExtension}");
			if (byMvid.Length > 0) {
				if (byMvid.Length > 1) throw new Exception($"Found multiple possible files for MVID {mvid} in directory {directory}");
				return byMvid[0];
			}
		}
		if (alias != null) {
			var byAlias = Directory.GetFiles(directory, $"*{alias}*.{fileExtension}");
			if (byAlias.Length > 0) {
				if (byAlias.Length > 1) throw new Exception($"Found multiple possible files for alias {alias} in directory {directory}");
				return mvid != null ? AddMvidToFileNameIfAbsent(mvid, byAlias[0]) : byAlias[0];
			}
		}
		throw new Exception($"Failed to find input file; mvid {mvid ?? "[none]"} alias {alias ?? "[none]"} extension {fileExtension} directory {directory}");
	}

	private static string AddMvidToFileNameIfAbsent(string mvid, string filePath) {
		if (filePath.Contains(mvid)) return filePath;
		var newFilePath = filePath.Insert(filePath.LastIndexOf("."), $"_{mvid}");
		File.Move(filePath, newFilePath);
		return newFilePath;
	}

	public static void Main(string[] args) {
		Directory.CreateDirectory(ExesDirectory);
		Directory.CreateDirectory(StringsDirectory);
		Directory.CreateDirectory(MappingsDirectory);

		Console.WriteLine(string.Join(", ", Directory.GetFiles(ExesDirectory)));

		var matchOntoSelf = false; // whether matching onto self (for testing match stability)

		var versionAliasA = "old";
		var versionAliasB = matchOntoSelf ? versionAliasA : "ce_skew_polymers";

		TypeClassifier.Init();
		MethodClassifier.Init();
		FieldClassifier.Init();
		MethodParamClassifier.Init();

		var options = new JsonSerializerOptions();
		options.IncludeFields = true;
		options.WriteIndented = true;

		var modulePathA = FindFileByMvidOrAlias(ExesDirectory, null, versionAliasA, "exe");
		var modulePathB = FindFileByMvidOrAlias(ExesDirectory, null, versionAliasB, "exe");

		var moduleA = ModuleDefinition.ReadModule(modulePathA);
		var moduleB = ModuleDefinition.ReadModule(modulePathB);

		var mvidA = moduleA.Mvid.ToString();
		var mvidB = moduleB.Mvid.ToString();

		if (!modulePathA.Contains(mvidA)) {
			moduleA.Dispose();
			modulePathA = AddMvidToFileNameIfAbsent(mvidA, modulePathA);
			moduleA = ModuleDefinition.ReadModule(modulePathA);
		}

		if (matchOntoSelf && modulePathB != modulePathA) {
			moduleB.Dispose();
			modulePathB = modulePathA;
			moduleB = ModuleDefinition.ReadModule(modulePathB);
			mvidB = moduleB.Mvid.ToString();
		} else if (!modulePathB.Contains(mvidB)) {
			moduleB.Dispose();
			modulePathB = AddMvidToFileNameIfAbsent(mvidB, modulePathB);
			moduleB = ModuleDefinition.ReadModule(modulePathB);
		}

		var stringsPathA = FindFileByMvidOrAlias(StringsDirectory, mvidA, versionAliasA, "csv");
		var stringsPathB = FindFileByMvidOrAlias(StringsDirectory, mvidB, versionAliasB, "csv");

		Mappings mappingsA;
		using (var mappingsStream = File.Open(FindFileByMvidOrAlias(MappingsDirectory, mvidA, versionAliasA, "json"), FileMode.Open)) {
			mappingsA = JsonSerializer.Deserialize<Mappings>(mappingsStream, options)!;
		}

		var matcher = new Matcher(mappingsA);
		matcher.Init(moduleA, moduleB, [stringsPathA], [stringsPathB]);
		matcher.AutoMatchAll(Console.WriteLine);

		var mappingsB = new Mappings() {
			NamespaceA = mappingsA.NamespaceA,
			NamespaceB = mappingsA.NamespaceB,
		};

		matcher.LogMissingMatches(true);

		// // index by name instead of fullname
		// Dictionary<string, TypeInstance?> matcherClassInstances = matcher.env.EnvA.types.Values.ToDictionary(typeInstance => typeInstance.GetName())!;
		// foreach (var classMapping in mappingsA.Classes) {
		// 	var classInstance = matcherClassInstances.GetValueOrDefault(classMapping.ClassNameA, null);
		// 	if (classInstance == null) {
		// 		// TODO these missing instance cases evidently do happen; should investigate why
		// 		Console.WriteLine($"Missing class instance for {classMapping.ClassNameA}; this shouldn't happen probably?");
		// 		continue;
		// 	}
		// 	if (classInstance.HasMatch()) {
		// 		TypeInstance matchedClassInstance = classInstance.GetMatch()!;
		// 		var matchedClassMapping = new ClassMapping() {
		// 			ClassNameA = matchedClassInstance.GetName(),
		// 			ClassNameB = classMapping.ClassNameB,
		// 		};
		// 		mappingsB.Classes.Add(matchedClassMapping);
		// 		foreach (var methodMapping in classMapping.Methods) {
		// 			var methodInstance = classInstance.GetMethod(methodMapping.MethodNameA, null);
		// 			if (methodInstance == null) {
		// 				Console.WriteLine($"Missing method instance for {methodMapping.MethodNameA}; this shouldn't happen probably?");
		// 				continue;
		// 			}
		// 			if (methodInstance.HasMatch()) {
		// 				MethodInstance matchedMethodInstance = methodInstance.GetMatch()!;
		// 				var matchedMethodMapping = new MethodMapping() {
		// 					MethodNameA = matchedMethodInstance.GetName(),
		// 					MethodNameB = methodMapping.MethodNameB,
		// 				};
		// 				matchedClassMapping.Methods.Add(matchedMethodMapping);
		// 				foreach (var methodParamMapping in methodMapping.Parameters) {
		// 					var methodParamInstance = methodInstance.args.Where(param => param.GetName() == methodParamMapping.ParameterNameA).SingleOrDefault((MethodParamInstance?) null);
		// 					if (methodParamInstance == null) {
		// 						Console.WriteLine($"Missing method param instance for {methodParamMapping.ParameterNameA}; this shouldn't happen probably?");
		// 						continue;
		// 					}
		// 					if (methodParamInstance.HasMatch()) {
		// 						MethodParamInstance matchedMethodParamInstance = methodParamInstance.GetMatch()!;
		// 						var matchedMethodParamMapping = new MethodParameterMapping() {
		// 							ParameterNameA = matchedMethodParamInstance.GetName(),
		// 							ParameterNameB = methodParamMapping.ParameterNameB,
		// 						};
		// 						matchedMethodMapping.Parameters.Add(matchedMethodParamMapping);
		// 					}
		// 				}
		// 				foreach (var genericParamMapping in methodMapping.GenericParameters) {
		// 					var genericParamInstance = methodInstance.genericParamsOrdered.Where(param => param.GetName() == genericParamMapping.GenericNameA).SingleOrDefault((TypeInstance?) null);
		// 					if (genericParamInstance == null) {
		// 						Console.WriteLine($"Missing generic param instance for {genericParamMapping.GenericNameA}; this shouldn't happen probably?");
		// 						continue;
		// 					}
		// 					if (genericParamInstance.HasMatch()) {
		// 						TypeInstance matchedGenericParamInstance = genericParamInstance.GetMatch()!;
		// 						var matchedGenericParamMapping = new GenericParameterMapping {
		// 							GenericNameA = matchedGenericParamInstance.GetName(),
		// 							GenericNameB = genericParamMapping.GenericNameB,
		// 						};
		// 						matchedMethodMapping.GenericParameters.Add(matchedGenericParamMapping);
		// 					}
		// 				}
		// 			}
		// 		}
		// 		foreach (var fieldMapping in classMapping.Fields) {
		// 			var fieldInstance = classInstance.GetField(fieldMapping.FieldNameA, null);
		// 			if (fieldInstance == null) {
		// 				Console.WriteLine($"Missing field instance for {fieldMapping.FieldNameA}; this shouldn't happen probably?");
		// 				continue;
		// 			}
		// 			if (fieldInstance.HasMatch()) {
		// 				FieldInstance matchedFieldInstance = fieldInstance.GetMatch()!;
		// 				var matchedFieldMapping = new FieldMapping() {
		// 					FieldNameA = matchedFieldInstance.GetName(),
		// 					FieldNameB = fieldMapping.FieldNameB,
		// 				};
		// 				matchedClassMapping.Fields.Add(matchedFieldMapping);
		// 			}
		// 		}
		// 		foreach (var genericParamMapping in classMapping.GenericParameters) {
		// 			var genericParamInstance = classInstance.genericParamsOrdered.Where(param => param.GetName() == genericParamMapping.GenericNameA).SingleOrDefault((TypeInstance?) null);
		// 			if (genericParamInstance == null) {
		// 				Console.WriteLine($"Missing generic param instance for {genericParamMapping.GenericNameA}; this shouldn't happen probably?");
		// 				continue;
		// 			}
		// 			if (genericParamInstance.HasMatch()) {
		// 				TypeInstance matchedGenericParamInstance = genericParamInstance.GetMatch()!;
		// 				var matchedGenericParamMapping = new GenericParameterMapping {
		// 					GenericNameA = matchedGenericParamInstance.GetName(),
		// 					GenericNameB = genericParamMapping.GenericNameB,
		// 				};
		// 				matchedClassMapping.GenericParameters.Add(matchedGenericParamMapping);
		// 			}
		// 		}
		// 	}
		// }
		// using (var mappingsStream = File.Open("selfMatchTest.json", FileMode.Create)) {
		// 	JsonSerializer.Serialize(mappingsStream, mappingsB, options);
		// }
	}
}
