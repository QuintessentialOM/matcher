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
}
