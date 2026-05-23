global using System;
global using System.Collections.Generic;
global using Mono.Collections.Generic;
using System.IO;
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
		var moduleA = ModuleDefinition.ReadModule("Lightning_old.exe");
		var moduleB = ModuleDefinition.ReadModule("Lightning_ce_skew_polymers.exe");
		var matcher = new Matcher();
		matcher.Init(moduleA, moduleB,
				"#=qb3HWBkVlFVubfVOAwuy8rw==",
				"#=qDID3KRmTOKqTiWqrwHq$pA==",
				["strings_old.csv"], ["strings_ce_skew_polymers.csv"]);
		matcher.AutoMatchAll(Console.WriteLine);

		var options = new JsonSerializerOptions();
		options.IncludeFields = true;
		options.WriteIndented = true;

		Mappings mappingsA;
		using (var mappingsStream = File.Open("oldVersionMappings.json", FileMode.Open)) {
			mappingsA = JsonSerializer.Deserialize<Mappings>(mappingsStream, options)!;
		}
		var mappingsB = new Mappings() {
			NamespaceA = mappingsA.NamespaceA,
			NamespaceB = mappingsA.NamespaceB,
		};
		Console.WriteLine(mappingsA.Classes.Count);
		foreach (var classMapping in mappingsA.Classes) {
			var classInstance = matcher.env.EnvA.types!.GetValueOrDefault(classMapping.ClassNameA, null);
			if (classInstance == null) {
				// TODO these missing instance cases evidently do happen; should investigate why
				Console.WriteLine($"Missing class instance for {classMapping.ClassNameA}; this shouldn't happen probably?");
				continue;
			}
			if (classInstance.HasMatch()) {
				TypeInstance matchedClassInstance = classInstance.GetMatch()!;
				var matchedClassMapping = new ClassMapping() {
					ClassNameA = matchedClassInstance.GetName(),
					ClassNameB = classMapping.ClassNameB,
				};
				mappingsB.Classes.Add(matchedClassMapping);
				foreach (var methodMapping in classMapping.Methods) {
					var methodInstance = classInstance.GetMethod(methodMapping.MethodNameA, null);
					if (methodInstance == null) {
						Console.WriteLine($"Missing method instance for {methodMapping.MethodNameA}; this shouldn't happen probably?");
						continue;
					}
					if (methodInstance.HasMatch()) {
						MethodInstance matchedMethodInstance = methodInstance.GetMatch()!;
						var matchedMethodMapping = new MethodMapping() {
							MethodNameA = matchedMethodInstance.GetName(),
							MethodNameB = methodMapping.MethodNameB,
						};
						matchedClassMapping.Methods.Add(matchedMethodMapping);
					}
				}
				foreach (var fieldMapping in classMapping.Fields) {
					var fieldInstance = classInstance.GetField(fieldMapping.FieldNameA, null);
					if (fieldInstance == null) {
						Console.WriteLine($"Missing field instance for {fieldMapping.FieldNameA}; this shouldn't happen probably?");
						continue;
					}
					if (fieldInstance.HasMatch()) {
						FieldInstance matchedFieldInstance = fieldInstance.GetMatch()!;
						var matchedFieldMapping = new FieldMapping() {
							FieldNameA = matchedFieldInstance.GetName(),
							FieldNameB = fieldMapping.FieldNameB,
						};
						matchedClassMapping.Fields.Add(matchedFieldMapping);
					}
				}
			}
		}
		using (var mappingsStream = File.Open("newVersionMappings.json", FileMode.Create)) {
			JsonSerializer.Serialize(mappingsStream, mappingsB, options);
		}
	}
}
