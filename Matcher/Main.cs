global using System;
global using System.Collections.Generic;
global using Mono.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Matcher.Matching;
using Matcher.Matching.Classifier;
using Mono.Cecil;

namespace Matcher;

public static class MatcherMain {
	public static void Main(string[] args) {
		TypeClassifier.Init();
		MethodClassifier.Init();
		FieldClassifier.Init();
		MethodParamClassifier.Init();

		var options = new JsonSerializerOptions();
		options.IncludeFields = true;
		options.WriteIndented = true;

		Mappings mappingsA;
		using (var mappingsStream = File.Open("oldVersionMappings.json", FileMode.Open)) {
			mappingsA = JsonSerializer.Deserialize<Mappings>(mappingsStream, options)!;
		}

		// Match against self for testing
		var moduleA = ModuleDefinition.ReadModule("Lightning_old.exe");
		var moduleB = ModuleDefinition.ReadModule("Lightning_old.exe");//ModuleDefinition.ReadModule("Lightning_ce_skew_polymers.exe");
		var matcher = new Matcher(mappingsA);
		// matcher.Init(moduleA, moduleB,
		// 		"#=qb3HWBkVlFVubfVOAwuy8rw==",
		// 		"#=qDID3KRmTOKqTiWqrwHq$pA==",
		// 		["strings_old.csv"], ["strings_ce_skew_polymers.csv"]);
		matcher.Init(moduleA, moduleB,
				"#=qb3HWBkVlFVubfVOAwuy8rw==",
				"#=qb3HWBkVlFVubfVOAwuy8rw==",
				["strings_old.csv"], ["strings_old.csv"]);
		matcher.AutoMatchAll(Console.WriteLine);

		var mappingsB = new Mappings() {
			NamespaceA = mappingsA.NamespaceA,
			NamespaceB = mappingsA.NamespaceB,
		};
		Console.WriteLine(mappingsA.Classes.Count);
		var classObfToInter = mappingsA.Classes.ToDictionary(cls => cls.ClassNameA);
		foreach (var cls in matcher.env.EnvA.types.Values) {
			if (cls.CecilType == null) continue;
			if (cls.CecilTypeReference.IsPointer || cls.CecilTypeReference.IsGenericInstance) continue;
			if (!cls.HasMatch()) Console.WriteLine($"unmatched {classObfToInter.GetValueOrDefault(cls.CecilTypeReference.Name)?.ClassNameB ?? "???"} ({cls.CecilTypeReference.FullName})");
		}
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
