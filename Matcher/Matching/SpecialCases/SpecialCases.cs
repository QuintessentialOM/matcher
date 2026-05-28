using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using Matcher.Matching;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Matcher.matching.SpecialCases;

public class SpecialCases {
	private readonly Matcher matcher;
	private readonly Mappings mappingsA;

	public SpecialCases(Matcher matcher, Mappings mappingsA) {
		this.matcher = matcher;
		this.mappingsA = mappingsA;
	}

	public void DoSpecialCaseMatches() {
		MatchTextures();
		MatchClass9();
		MatchClass111();
		IgnoreSDL();
		IgnoreUnusedNonnestedEnums();
	}

	private TypeInstance FindTypeAFromIntermediary(string intermediaryName) {
		var obfName = mappingsA.Classes.Where(cls => cls.ClassNameB == intermediaryName).Single().ClassNameA;
		return matcher.env.EnvA.types.Values.Where(type => type.CecilTypeReference.Name == obfName).Single();
	}

	private MethodInstance FindMethodAFromIntermediary(TypeInstance typeA, string intermediaryName) {
		var obfName = mappingsA.Classes.Where(cls => cls.ClassNameA == typeA.CecilTypeReference.Name).Single().Methods.Where(method => method.MethodNameB == intermediaryName).Single().MethodNameA;
		return typeA.GetMethod(obfName, null)!;
	}

	private string GetIntermediaryForTypeA(TypeInstance typeInstance) {
		return GetIntermediaryForTypeA(typeInstance.CecilTypeReference);
	}

	private string GetIntermediaryForTypeA(TypeReference typeReference) {
		var cls = mappingsA.Classes.Where(cls => cls.ClassNameA == typeReference.Name).SingleOrDefault((ClassMapping?) null);
		return cls?.ClassNameB ?? "???";
	}

	private string GetIntermediaryForFieldA(FieldInstance fieldInstance) {
		return GetIntermediaryForFieldA(fieldInstance.CecilField);
	}

	private string GetIntermediaryForFieldA(FieldReference fieldReference) {
		var cls = mappingsA.Classes.Where(cls => cls.ClassNameA == fieldReference.DeclaringType.Name).SingleOrDefault((ClassMapping?) null);
		if (cls == null) return "???";
		var fieldName = cls.Fields.Where(field => field.FieldNameA == fieldReference.Name).SingleOrDefault((FieldMapping?) null);
		return fieldName?.FieldNameB ?? "???";
	}

	private void MatchTextures() {
		// This method is currently rather brittle but hopefully nothing in the Textures class will change enough to break it :)

		const string texturesIntermediary = "class_17";
		// const string textureIntermediary = "class_256";
		// const string assetLoadingIntermediary = "class_235";
		// const string flatColorMethodIntermediary = "method_618";
		// const string loadTextureMethodIntermediary = "method_615";

		Regex frameCount = new("_[0-9]{4}");

		var texturesClassA = FindTypeAFromIntermediary(texturesIntermediary);
		// var textureClassA = FindTypeAFromIntermediary(textureIntermediary);
		// var assetLoadingClassA = FindTypeAFromIntermediary(assetLoadingIntermediary);

		// var flatColorMethod = FindMethodAFromIntermediary(assetLoadingClassA, flatColorMethodIntermediary);
		// var loadTextureMethod = FindMethodAFromIntermediary(assetLoadingClassA, loadTextureMethodIntermediary);

		var texturesClassB = texturesClassA.GetMatch();
		if (texturesClassB == null) throw new Exception("textures class match not found");

		var textureFieldsByIdA = new Dictionary<string, FieldReference>();

		var textureFieldsByIdB = new Dictionary<string, FieldReference>();

		var lastString = "";


		foreach (var method in texturesClassA.methodsOrdered) {
			if (method.CecilMethod?.Body?.Instructions != null) {
				foreach (var instr in method.CecilMethod.Body.Instructions) {
					if (instr.OpCode == OpCodes.Ldstr) {
						// strip frame numbers so changed frame counts don't prevent matching
						lastString = frameCount.Replace((string) instr.Operand, "");
					} else if (instr.OpCode == OpCodes.Ldsfld && instr.Operand is FieldReference fieldRef) {
						if (fieldRef.DeclaringType.Name != "Color") continue;
						lastString = $"__color__{fieldRef.Name}";
					} else if (instr.OpCode == OpCodes.Stfld) {
						// TODO validate field type
						if (lastString == "") continue; // TODO hack to avoid breaking on random field init code at the head of the method
						textureFieldsByIdA.Add(lastString, (FieldReference) instr.Operand);
					}
				}
			}
		}

		lastString = "";

		foreach (var method in texturesClassB.methodsOrdered) {
			if (method.CecilMethod?.Body?.Instructions != null) {
				foreach (var instr in method.CecilMethod.Body.Instructions) {
					if (instr.OpCode == OpCodes.Ldstr) {
						// strip frame numbers so changed frame counts don't prevent matching
						lastString = frameCount.Replace((string) instr.Operand, "");
					} else if (instr.OpCode == OpCodes.Ldsfld && instr.Operand is FieldReference fieldRef) {
						if (fieldRef.DeclaringType.Name != "Color") continue;
						lastString = $"__color__{fieldRef.Name}";
					} else if (instr.OpCode == OpCodes.Stfld) {
						// TODO validate field type
						if (lastString == "") continue; // TODO hack to avoid breaking on random field init code at the head of the method
						textureFieldsByIdB.Add(lastString, (FieldReference) instr.Operand);
					}
				}
			}
		}

		Dictionary<FieldReference, FieldReference> fieldMatchCandidates = new();
		Dictionary<TypeReference, TypeReference> typeMatchCandidates = new();

		foreach (var textureKey in textureFieldsByIdA.Keys.Union(textureFieldsByIdB.Keys)) {
			if (!textureFieldsByIdA.ContainsKey(textureKey)) {
				Console.WriteLine($"Unmatched texture field in assembly B: {textureKey} -> {textureFieldsByIdB[textureKey].FullName}");
			} else if (!textureFieldsByIdB.ContainsKey(textureKey)) {
				Console.WriteLine($"Unmatched texture field in assembly A: {textureKey} -> {GetIntermediaryForFieldA(textureFieldsByIdA[textureKey])} ({textureFieldsByIdA[textureKey].FullName})");
			} else {
				fieldMatchCandidates.Add(textureFieldsByIdA[textureKey], textureFieldsByIdB[textureKey]);
			}
		}

		var typeQueue = new Queue<(TypeReference, TypeReference)>(fieldMatchCandidates.Select(pair => (pair.Key.DeclaringType, pair.Value.DeclaringType)));

		while (typeQueue.Count > 0) {
			var pair = typeQueue.Dequeue();
			if (pair.Item1.Name == texturesClassA.CecilTypeReference.Name) {
				if (pair.Item2.Name != texturesClassB.CecilTypeReference.Name) {
					throw new Exception("Reached Textures class in one hierarchy but not the other; folder structure may have changed (TODO handle this case properly)");
				}
				continue;
			}
			if (pair.Item1.DeclaringType == null || pair.Item2.DeclaringType == null)
				throw new Exception("Missing outer class for class; this shouldn't hpapen?");
			typeMatchCandidates[pair.Item1] = pair.Item2;
			typeQueue.Enqueue((pair.Item1.DeclaringType, pair.Item2.DeclaringType));
		}

		// match classes first
		foreach (var pair in typeMatchCandidates) {
			matcher.MatchType(matcher.env.EnvA.GetCreateTypeInstance(pair.Key), matcher.env.EnvB.GetCreateTypeInstance(pair.Value));
		}
		
		// then fields
		foreach (var pair in fieldMatchCandidates) {
			matcher.MatchField(
					matcher.env.EnvA.GetCreateTypeInstance(pair.Key.DeclaringType).GetField(pair.Key.Name, pair.Key.FieldType.Name)!,
					matcher.env.EnvB.GetCreateTypeInstance(pair.Value.DeclaringType).GetField(pair.Value.Name, pair.Value.FieldType.Name)!
			);
		}

		Console.WriteLine($"Matched {fieldMatchCandidates.Count} fields and {typeMatchCandidates.Count} inner classes in Textures");
	}

	private void MatchClass9() {
		const string class9Intermediary = "class_9";

		var class9A = FindTypeAFromIntermediary(class9Intermediary);
		var class9B = class9A.GetMatch();
		if (class9B == null) throw new Exception("class_9 match not found");

		// structs inside class_9 are uniquely identified by their size. currently, at least.
		var structsABySize = new Dictionary<int, TypeInstance>();
		var structsBBySize = new Dictionary<int, TypeInstance>();

		class9A.nestedTypes.ForEach(type => {
			structsABySize.Add(type.CecilType.ClassSize, type);
		});
		class9B.nestedTypes.ForEach(type => {
			structsBBySize.Add(type.CecilType.ClassSize, type);
		});

		int count = 0;

		foreach (var size in structsABySize.Keys.Union(structsBBySize.Keys)) {
			if (!structsABySize.ContainsKey(size)) {
				Console.WriteLine($"Unmatched struct in assembly B: {structsBBySize[size].CecilTypeReference.FullName}");
			} else if (!structsBBySize.ContainsKey(size)) {
				Console.WriteLine($"Unmatched struct in assembly A: {GetIntermediaryForTypeA(structsABySize[size])} ({structsABySize[size].CecilTypeReference.FullName})");
			} else {
				matcher.MatchType(structsABySize[size], structsBBySize[size]);
				count++;
			}
		}

		Console.WriteLine($"Matched {count} structs in class_9");

		// Identify fields by their initialization data
		var fieldsAByData = new Dictionary<string, FieldInstance>();
		var fieldsBByData = new Dictionary<string, FieldInstance>();

		foreach (var field in class9A.fieldsOrdered) {
			fieldsAByData.Add(BitConverter.ToString(field.CecilField.InitialValue), field);
		}
		foreach (var field in class9B.fieldsOrdered) {
			fieldsBByData.Add(BitConverter.ToString(field.CecilField.InitialValue), field);
		}

		count = 0;

		foreach (var data in fieldsAByData.Keys.Union(fieldsBByData.Keys)) {
			if (!fieldsAByData.ContainsKey(data)) {
				Console.WriteLine($"Unmatched field in assembly B: {fieldsBByData[data].CecilField!.FullName}");
			} else if (!fieldsBByData.ContainsKey(data)) {
				Console.WriteLine($"Unmatched field in assembly A: {GetIntermediaryForFieldA(fieldsAByData[data])} ({fieldsAByData[data].CecilField!.FullName})");
			} else {
				matcher.MatchField(fieldsAByData[data], fieldsBByData[data]);
				count++;
			}
		}

		Console.WriteLine($"Matched {count} static fields in class_9");
	}

	private void MatchClass111() {
		// Class with GL method bindings - has a bunch of fields and delegate types corresponding to gl functions, and some mystery enums
		const string class111Intermediary = "class_111";
		// const string method149Intermediary = "method_149";
		const string method150Intermediary = "method_150";

		var class111A = FindTypeAFromIntermediary(class111Intermediary);
		var class111B = class111A.GetMatch();
		if (class111B == null) throw new Exception("class_111 match not found");

		// var method149A = FindMethodAFromIntermediary(class111A, method149Intermediary);
		var method150A = FindMethodAFromIntermediary(class111A, method150Intermediary);

		// var method149B = method149A.GetMatch();
		var method150B = method150A.GetMatch();

		if (method150B == null) throw new Exception("method_150 match not found");

		var glMethodNameToFieldA = new Dictionary<string, FieldReference>();
		var glMethodNameToFieldB = new Dictionary<string, FieldReference>();

		var lastString = "";
		foreach (var instr in method150A.CecilMethod.Body.Instructions) {
			if (instr.OpCode == OpCodes.Ldstr) {
				lastString = (string) instr.Operand;
			} else if (instr.OpCode == OpCodes.Stsfld) {
				// TODO validate field?
				if (lastString == "") continue;
				glMethodNameToFieldA.Add(lastString, (FieldReference) instr.Operand);
			}
		}

		lastString = "";
		foreach (var instr in method150B.CecilMethod.Body.Instructions) {
			if (instr.OpCode == OpCodes.Ldstr) {
				lastString = (string) instr.Operand;
			} else if (instr.OpCode == OpCodes.Stsfld) {
				// TODO validate field?
				if (lastString == "") continue;
				glMethodNameToFieldB.Add(lastString, (FieldReference) instr.Operand);
			}
		}

		var count = 0;
		var enumCount = 0;
		foreach (var data in glMethodNameToFieldA.Keys.Union(glMethodNameToFieldB.Keys)) {
			if (!glMethodNameToFieldA.ContainsKey(data)) {
				Console.WriteLine($"Unmatched gl function in assembly B: {glMethodNameToFieldB[data].FullName}");
			} else if (!glMethodNameToFieldB.ContainsKey(data)) {
				Console.WriteLine($"Unmatched gl function in assembly A: {GetIntermediaryForFieldA(glMethodNameToFieldA[data])} ({glMethodNameToFieldA[data].FullName})");
			} else {
				var delegateA = matcher.env.EnvA.GetCreateTypeInstance(glMethodNameToFieldA[data].FieldType);
				var delegateB = matcher.env.EnvB.GetCreateTypeInstance(glMethodNameToFieldB[data].FieldType);
				matcher.MatchType(delegateA, delegateB);
				matcher.MatchField(
						class111A.GetField(glMethodNameToFieldA[data].Name, glMethodNameToFieldA[data].FieldType.Name)!,
						class111B.GetField(glMethodNameToFieldB[data].Name, glMethodNameToFieldB[data].FieldType.Name)!);
				count++;

				var invokeA = delegateA.GetMethod("Invoke", null);
				var invokeB = delegateA.GetMethod("Invoke", null);
				foreach (var (paramA, paramB) in invokeA!.args.Zip(invokeB!.args)) {
					if (!paramA.paramType.HasMatch() && !paramB.paramType.HasMatch() && paramA.paramType.GetSubgroup() == TypeSubgroup.Enum && paramB.paramType.GetSubgroup() == TypeSubgroup.Enum) {
						matcher.MatchType(paramA.paramType, paramB.paramType);
						enumCount++;
					}
				}
				if (!invokeA.returnType.HasMatch() && !invokeB.returnType.HasMatch() && invokeA.returnType.GetSubgroup() == TypeSubgroup.Enum && invokeB.returnType.GetSubgroup() == TypeSubgroup.Enum) {
					matcher.MatchType(invokeA.returnType, invokeB.returnType);
					enumCount++;
				}
			}
		}

		// Exclude unused enums from matching

		var enumIgnoreCountA = 0;
		foreach (var nested in class111A.nestedTypes) {
			if (!nested.IsIgnored() && nested.GetSubgroup() == TypeSubgroup.Enum) {
				nested.MarkIgnored();
				enumIgnoreCountA++;
			}
		}
		var enumIgnoreCountB = 0;
		foreach (var nested in class111B.nestedTypes) {
			if (!nested.IsIgnored() && nested.GetSubgroup() == TypeSubgroup.Enum) {
				nested.MarkIgnored();
				enumIgnoreCountB++;
			}
		}

		Console.WriteLine($"Matched {count} fields/delegates and {enumCount} enums, skipped {enumIgnoreCountA} (A)/{enumIgnoreCountB} (B) enums in class_111");
	}

	private void IgnoreSDL() {
		// We ignore SDL for now, since most of it is unobfuscated anyway, and the obfuscated parts are awkward to match
		matcher.env.EnvA.types["SDL2.SDL"].MarkIgnoredRecursive();
		matcher.env.EnvB.types["SDL2.SDL"].MarkIgnoredRecursive();
		Console.WriteLine("Ignoring SDL");
	}

	private bool CheckEnumUnused(TypeInstance cls) {
		// TODO this could mark enums as unused even if they appear as local variables in methods. Probably fine?
		return cls.fieldTypeRefs.Count == 0 && cls.methodTypeRefs.Count == 0;
	}

	private void IgnoreUnusedNonnestedEnums() {
		var enumIgnoreCountA = 0;
		foreach (var cls in matcher.env.EnvA.types.Values) {
			if (!cls.IsIgnored() && cls.outerType == null && cls.GetSubgroup() == TypeSubgroup.Enum && CheckEnumUnused(cls)) {
				cls.MarkIgnored();
				enumIgnoreCountA++;
			}
		}
		var enumIgnoreCountB = 0;
		foreach (var cls in matcher.env.EnvB.types.Values) {
			if (!cls.IsIgnored() && cls.outerType == null && cls.GetSubgroup() == TypeSubgroup.Enum && CheckEnumUnused(cls)) {
				cls.MarkIgnored();
				enumIgnoreCountB++;
			}
		}

		Console.WriteLine($"Ignoring {enumIgnoreCountA} (A)/{enumIgnoreCountB} (B) unused non-nested enums");
	}
}
