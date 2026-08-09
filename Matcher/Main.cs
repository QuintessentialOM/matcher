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
	private static readonly string IntermediaryMappingsDirectory = "run/mappings";
	private static readonly string NamedMappingsDirectory = "run/named";


	private static string? MaybeFindFileByMvidOrAlias(string directory, string? mvid, string? alias, string fileExtension) {
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
		return null;
	}

	private static string FindFileByMvidOrAlias(string directory, string? mvid, string? alias, string fileExtension) {
		return MaybeFindFileByMvidOrAlias(directory, mvid, alias, fileExtension) ?? throw new Exception($"Failed to find input file; mvid {mvid ?? "[none]"} alias {alias ?? "[none]"} extension {fileExtension} directory {directory}");
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
		Directory.CreateDirectory(IntermediaryMappingsDirectory);
		Directory.CreateDirectory(NamedMappingsDirectory);

		var matchOntoSelf = false; // whether matching onto self (for testing match stability)

		var versionAliasA = "ce_20260709_russianJournal";
		var versionAliasB = matchOntoSelf ? versionAliasA : "ce_20260729";

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

		var stringsPathA = MaybeFindFileByMvidOrAlias(StringsDirectory, mvidA, versionAliasA, "csv") ?? StringDumping.DumpStrings(StringsDirectory, mvidA, versionAliasA, moduleA);
		var stringsPathB = MaybeFindFileByMvidOrAlias(StringsDirectory, mvidB, versionAliasB, "csv") ?? StringDumping.DumpStrings(StringsDirectory, mvidB, versionAliasB, moduleB);

		Mappings? mappingsOld;
		using (var mappingsStream = File.Open(FindFileByMvidOrAlias(IntermediaryMappingsDirectory, mvidA, versionAliasA, "json"), FileMode.Open)) {
			mappingsOld = JsonSerializer.Deserialize<Mappings>(mappingsStream, options)!;
		}

		// pairs of intermediary names for A and obf names for B, separated by commas
		var hintsFile = Path.Join(RunDirectory, "hints.txt");
		Dictionary<string, string>? matchHints = null;
		if (File.Exists(hintsFile)) {
			matchHints = [];
			foreach (var line in File.ReadAllLines(hintsFile)) {
				var trimmed = line.Trim();
				if (trimmed == "" || trimmed.StartsWith("#")) continue;
				var split = trimmed.Split(",");
				matchHints[split[0]] = split[1];
			}
		}

		var matcher = new Matcher(mappingsOld, matchHints);
		matcher.Init(moduleA, moduleB, [stringsPathA], [stringsPathB]);
		matcher.AutoMatchAll(Console.WriteLine);

		var mappingsNew = new Mappings() {
			NamespaceA = "obf",
			NamespaceB = "intermediary",
			nextClassIndex = mappingsOld.nextClassIndex,
			nextEnumIndex = mappingsOld.nextEnumIndex,
			nextInterfaceIndex = mappingsOld.nextInterfaceIndex,
			nextMethodIndex = mappingsOld.nextMethodIndex,
			nextStructIndex = mappingsOld.nextStructIndex,
			nextDelegateIndex = mappingsOld.nextDelegateIndex,
			nextFieldIndex = mappingsOld.nextFieldIndex,
			nextGenericIndex = mappingsOld.nextGenericIndex,
			nextParamIndex = mappingsOld.nextParamIndex,
		};

		matcher.LogMissingMatches(true);

		// // Ignore non-obf names, except for the generated <> stuff. <Module> is not renamed as this breaks things.
		var deobfPattern = new Regex("^(?:[a-zA-Z_\\`][a-zA-Z0-9_\\`]*(\\[])*)|<Module>$");

		var methodHierarchyAToIntermediaryName = new Dictionary<MethodHierarchyData, string?>();
		var methodHierarchyBToNewlyGeneratedIntermediaryName = new Dictionary<MethodHierarchyData, string>();

		StringBuilder generatedNamedMappings = new("Mapping version: 0.1.0\n\n");

		// preprocessing to collect intermediary names for method hierarchies
		foreach (var type in matcher.env.EnvA.types.Values) {
			if (type.IsIgnored() || type.IsArray() || type.CecilTypeReference.IsPointer || type.CecilTypeReference.IsByReference) continue;
			if (type.CecilTypeReference.IsGenericInstance) continue;
			if (type.CecilTypeReference.IsGenericParameter) continue; // stored on their owner instead
			ClassMapping? classMapping = mappingsOld.Classes.Where(cls => cls.ClassFullNameA == type.CecilTypeReference.FullName).SingleOrDefault((ClassMapping?) null);
			if (classMapping == null) continue;
			foreach (var method in type.methodsOrdered) {
				MethodMapping? methodMapping = classMapping.Methods.Where(methodM => {
						return methodM.MethodNameA == method.CecilMethod.Name
							&& methodM.ReturnTypeFullNameA == method.CecilMethod.ReturnType.FullName
							&& methodM.ArgumentTypeFullNamesA.Count == method.CecilMethod.Parameters.Count
							&& methodM.ArgumentTypeFullNamesA.Zip(method.CecilMethod.Parameters).All(pair => pair.First == pair.Second.ParameterType.FullName);
					}).SingleOrDefault((MethodMapping?) null);
				if (methodHierarchyAToIntermediaryName.ContainsKey(method.hierarchyData)) {
					if (methodHierarchyAToIntermediaryName[method.hierarchyData] != methodMapping.MethodNameB)
						throw new Exception("Expected method intermediary names to match for all members of hierarchy");
				} else {
					methodHierarchyAToIntermediaryName[method.hierarchyData] = methodMapping.MethodNameB;
				}
			}
		}

		foreach (var type in matcher.env.EnvB.types.Values) {
			if (type.IsIgnored() || type.IsArray() || type.CecilTypeReference.IsPointer || type.CecilTypeReference.IsByReference) continue;
			if (type.CecilTypeReference.IsGenericInstance) continue;
			if (type.CecilTypeReference.IsGenericParameter) continue; // stored on their owner instead

			StringBuilder generatedNamedMappingsForType = new();

			ClassMapping? classMappingOld = null;
			if (type.HasMatch()) {
				classMappingOld = mappingsOld.Classes.Where(cls => cls.ClassFullNameA == type.GetMatch()!.CecilTypeReference.FullName).SingleOrDefault((ClassMapping?) null);
			}
			var classMappingNew = new ClassMapping {
				ClassFullNameA = type.CecilTypeReference.FullName,
			};
			mappingsNew.Classes.Add(classMappingNew);
			if (!deobfPattern.IsMatch(type.CecilTypeReference.Name)) {
				if (classMappingOld == null || classMappingOld.ClassNameB == null) {
					classMappingNew.ClassNameB = mappingsNew.GetNextTypeIntermediaryName(type.CecilType);
				} else {
					classMappingNew.ClassNameB = classMappingOld.ClassNameB;
				}
			}
			if (type.SuggestedMappedName != null) generatedNamedMappingsForType.AppendLine($"{classMappingNew.ClassNameB},{type.SuggestedMappedName}");
			foreach (var field in type.fieldsOrdered) {
				FieldMapping? fieldMappingOld = null;
				if (field.HasMatch()) {
					fieldMappingOld = classMappingOld?.Fields.Where(fieldM => fieldM.FieldNameA == field.GetMatch()!.CecilField.Name).SingleOrDefault((FieldMapping?) null);
				}
				var fieldMappingNew = new FieldMapping {
					FieldNameA = field.CecilField.Name,
				};
				classMappingNew.Fields.Add(fieldMappingNew);
				if (!deobfPattern.IsMatch(field.CecilField.Name)) {
					if (fieldMappingOld == null || fieldMappingOld.FieldNameB == null) {
						fieldMappingNew.FieldNameB = mappingsNew.GetNextFieldIntermediaryName();
					} else {
						fieldMappingNew.FieldNameB = fieldMappingOld.FieldNameB;
					}
				}
				if (field.SuggestedMappedName != null) generatedNamedMappingsForType.AppendLine($"{fieldMappingNew.FieldNameB},{field.SuggestedMappedName}");
			}
			foreach (var generic in type.genericParamsOrdered) {
				GenericParameterMapping? genericMappingOld = null;
				if (generic.HasMatch()) {
					genericMappingOld = classMappingOld?.GenericParameters.Where(genericM => genericM.GenericNameA == generic.GetMatch()!.CecilTypeReference.Name).SingleOrDefault((GenericParameterMapping?) null);
				}
				var genericMappingNew = new GenericParameterMapping {
					GenericNameA = generic.CecilTypeReference.Name,
				};
				classMappingNew.GenericParameters.Add(genericMappingNew);
				if (!deobfPattern.IsMatch(generic.CecilTypeReference.Name)) {
					if (genericMappingOld == null || genericMappingOld.GenericNameB == null) {
						genericMappingNew.GenericNameB = mappingsNew.GetNextGenericIntermediaryName();
					} else {
						genericMappingNew.GenericNameB = genericMappingOld.GenericNameB;
					}
				}
				if (generic.SuggestedMappedName != null) generatedNamedMappingsForType.AppendLine($"{genericMappingNew.GenericNameB},{generic.SuggestedMappedName}");
			}
			foreach (var method in type.methodsOrdered) {
				MethodMapping? methodMappingOld = null;
				if (method.HasMatch()) {
					var match = method.GetMatch()!.CecilMethod;
					methodMappingOld = classMappingOld?.Methods.Where(methodM => {
						return methodM.MethodNameA == match.Name
							&& methodM.ReturnTypeFullNameA == match.ReturnType.FullName
							&& methodM.ArgumentTypeFullNamesA.Count == match.Parameters.Count
							&& methodM.ArgumentTypeFullNamesA.Zip(match.Parameters).All(pair => pair.First == pair.Second.ParameterType.FullName);
					}).SingleOrDefault((MethodMapping?) null);
				}
				var methodMappingNew = new MethodMapping {
					MethodNameA = method.CecilMethod.Name,
					ReturnTypeFullNameA = method.returnType.CecilTypeReference.FullName,
					ArgumentTypeFullNamesA = method.args.Select(arg => arg.CecilParameter.ParameterType.FullName).ToList(),
				};
				classMappingNew.Methods.Add(methodMappingNew);
				if (!method.CecilMethod.IsRuntimeSpecialName && !deobfPattern.IsMatch(method.CecilMethod.Name)) {
					if (!method.HasHierarchyMatch() || methodHierarchyAToIntermediaryName[method.hierarchyData.MatchedHierarchy!] == null) {
						string? newNameForHierarchy = methodHierarchyBToNewlyGeneratedIntermediaryName!.GetValueOrDefault(method.hierarchyData, null);
						if (newNameForHierarchy != null) {
							methodMappingNew.MethodNameB = newNameForHierarchy;
						} else {
							methodMappingNew.MethodNameB = mappingsNew.GetNextMethodIntermediaryName();
							methodHierarchyBToNewlyGeneratedIntermediaryName[method.hierarchyData] = methodMappingNew.MethodNameB;
						}
					} else {
						methodMappingNew.MethodNameB = methodHierarchyAToIntermediaryName[method.hierarchyData.MatchedHierarchy!];
					}
				}
				if (method.SuggestedMappedName != null) generatedNamedMappingsForType.AppendLine($"{methodMappingNew.MethodNameB},{method.SuggestedMappedName}");

				foreach (var generic in method.genericParamsOrdered) {
					GenericParameterMapping? genericMappingOld = null;
					if (generic.HasMatch()) {
						genericMappingOld = methodMappingOld?.GenericParameters.Where(genericM => genericM.GenericNameA == generic.GetMatch()!.CecilTypeReference.Name).SingleOrDefault((GenericParameterMapping?) null);
					}
					var genericMappingNew = new GenericParameterMapping {
						GenericNameA = generic.CecilTypeReference.Name,
					};
					methodMappingNew.GenericParameters.Add(genericMappingNew);
					if (!deobfPattern.IsMatch(generic.CecilTypeReference.Name)) {
						if (genericMappingOld == null || genericMappingOld.GenericNameB == null) {
							genericMappingNew.GenericNameB = mappingsNew.GetNextGenericIntermediaryName();
						} else {
							genericMappingNew.GenericNameB = genericMappingOld.GenericNameB;
						}
					}
					if (generic.SuggestedMappedName != null) generatedNamedMappingsForType.AppendLine($"{genericMappingNew.GenericNameB},{generic.SuggestedMappedName}");
				}

				foreach (var param in method.args) {
					MethodParameterMapping? paramMappingOld = null;
					if (param.HasMatch()) {
						paramMappingOld = methodMappingOld?.Parameters.Where(paramM => paramM.ParameterNameA == param.GetMatch()!.CecilParameter.Name).SingleOrDefault((MethodParameterMapping?) null);
					}
					var paramMappingNew = new MethodParameterMapping {
						ParameterNameA = param.CecilParameter.Name,
					};
					methodMappingNew.Parameters.Add(paramMappingNew);
					if (!deobfPattern.IsMatch(param.CecilParameter.Name)) {
						if (paramMappingOld == null || paramMappingOld.ParameterNameB == null) {
							paramMappingNew.ParameterNameB = mappingsNew.GetNextParamIntermediaryName();
						} else {
							paramMappingNew.ParameterNameB = paramMappingOld.ParameterNameB;
						}
					}
					if (param.SuggestedMappedName != null) generatedNamedMappingsForType.AppendLine($"{paramMappingNew.ParameterNameB},{param.SuggestedMappedName}");
				}
			}
			if (generatedNamedMappingsForType.Length > 0) {
				generatedNamedMappings.AppendLine();
				generatedNamedMappings.AppendLine($"## {type.SuggestedMappedName ?? classMappingNew.ClassNameB}");
				generatedNamedMappings.Append(generatedNamedMappingsForType);
			}
		}

		if (!matchOntoSelf) {
			using (var mappingsStream = File.Open(Path.Join(IntermediaryMappingsDirectory, $"mappings_{versionAliasB}_{mvidB}.json"), FileMode.Create)) {
				JsonSerializer.Serialize(mappingsStream, mappingsNew, options);
			}
		}
		File.WriteAllText(Path.Join(NamedMappingsDirectory, $"named_{versionAliasB}_{mvidB}.txt"), generatedNamedMappings.ToString());
	}
}
