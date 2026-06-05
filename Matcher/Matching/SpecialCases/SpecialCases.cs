using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using Matcher.Matching;
using Matcher.Matching.Classifier;
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
		MatchClass246AndClass247();
		MatchClass125AndClass213();
		MatchSteamCallbacks();
		IgnoreSDL();
		IgnoreUnusedNonnestedEnums();
		MatchUnmatchedLambdaGeneratedClasses();
		MatchAngleBracketsCInnerClassMembers();
		MatchMiscObfInnerClassMembers();
	}

	private TypeInstance FindTypeAFromIntermediary(string intermediaryName) {
		var obfName = mappingsA.Classes.Where(cls => cls.ClassNameB == intermediaryName).Single().ClassNameA;
		return matcher.env.EnvA.types.Values.Where(type => type.CecilTypeReference.Name == obfName).Single();
	}

	private MethodInstance FindMethodAFromIntermediary(TypeInstance typeA, string intermediaryName) {
		var obfName = mappingsA.Classes.Where(cls => cls.ClassNameA == typeA.CecilTypeReference.Name).Single().Methods.Where(method => method.MethodNameB == intermediaryName).Single().MethodNameA;
		return typeA.GetMethod(obfName, null)!;
	}

	private FieldInstance FindFieldAFromIntermediary(TypeInstance typeA, string intermediaryName) {
		var obfName = mappingsA.Classes.Where(cls => cls.ClassNameA == typeA.CecilTypeReference.Name).Single().Fields.Where(field => field.FieldNameB == intermediaryName).Single().FieldNameA;
		return typeA.GetField(obfName, null)!;
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

	private string GetIntermediaryForMethodA(MethodInstance methodInstance) {
		return GetIntermediaryForMethodA(methodInstance.CecilMethod);
	}

	private string GetIntermediaryForMethodA(MethodReference methodReference) {
		var cls = mappingsA.Classes.Where(cls => cls.ClassNameA == methodReference.DeclaringType.Name).SingleOrDefault((ClassMapping?) null);
		if (cls == null) return "???";
		var methodName = cls.Methods.Where(method => method.MethodNameA == methodReference.Name).SingleOrDefault((MethodMapping?) null);
		return methodName?.MethodNameB ?? "???";
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

		// TODO this loop evaluates tree nodes multiple times since it doesn't check which have been visited already but whatever
		while (typeQueue.Count > 0) {
			var (typeA, typeB) = typeQueue.Dequeue();
			if (typeA.Name == texturesClassA.CecilTypeReference.Name) {
				if (typeB.Name != texturesClassB.CecilTypeReference.Name) {
					throw new Exception("Reached Textures class in one hierarchy but not the other; folder structure may have changed (TODO handle this case properly)");
				}
				continue;
			}
			if (typeA.DeclaringType == null || typeB.DeclaringType == null)
				throw new Exception("Missing outer class for class; this shouldn't happen?");
			typeMatchCandidates[typeA] = typeB;
			typeQueue.Enqueue((typeA.DeclaringType, typeB.DeclaringType));
			// fields on the outer class, with type of inner class
			var fieldA = typeA.DeclaringType.Resolve().Fields.Where(field => field.FieldType == typeA).Single();
			var fieldB = typeB.DeclaringType.Resolve().Fields.Where(field => field.FieldType == typeB).Single();
			fieldMatchCandidates[fieldA] = fieldB;
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
				var invokeB = delegateB.GetMethod("Invoke", null);
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

	private void MatchClass246AndClass247() {
		// Class that wraps D3D11 methods
		const string class246Intermediary = "class_246";
		// Class with bindings to D3D11 methods; inner class of the above class
		const string class247Intermediary = "class_247";

		var class246A = FindTypeAFromIntermediary(class246Intermediary);
		var class246B = class246A.GetMatch() ?? throw new Exception("class_246 match not found");
		var class247A = FindTypeAFromIntermediary(class247Intermediary);
		var class247B = class247A.GetMatch() ?? throw new Exception("class_247 match not found");

		var dllMethodsByEntrypointA = new Dictionary<string, MethodInstance>();
		var dllMethodsByEntrypointB = new Dictionary<string, MethodInstance>();

		foreach (var method in class247A.methodsOrdered) {
			if (method.CecilMethod!.PInvokeInfo != null)
				dllMethodsByEntrypointA.Add(method.CecilMethod!.PInvokeInfo.EntryPoint, method);
		}
		foreach (var method in class247B.methodsOrdered) {
			if (method.CecilMethod!.PInvokeInfo != null)
				dllMethodsByEntrypointB.Add(method.CecilMethod!.PInvokeInfo.EntryPoint, method);
		}

		var count = 0;
		foreach (var entrypoint in dllMethodsByEntrypointA.Keys.Union(dllMethodsByEntrypointB.Keys)) {
			if (!dllMethodsByEntrypointA.ContainsKey(entrypoint)) {
				Console.WriteLine($"Unmatched method in assembly B: {dllMethodsByEntrypointB[entrypoint].CecilMethod!.FullName}");
			} else if (!dllMethodsByEntrypointB.ContainsKey(entrypoint)) {
				Console.WriteLine($"Unmatched method in assembly A: {GetIntermediaryForMethodA(dllMethodsByEntrypointA[entrypoint])} ({dllMethodsByEntrypointA[entrypoint].CecilMethod!.FullName})");
			} else {
				matcher.MatchMethod(
					dllMethodsByEntrypointA[entrypoint],
					dllMethodsByEntrypointB[entrypoint]
				);
				count++;
			}
		}
		Console.WriteLine($"Matched {count} methods on class_247");

		var entrypointByDllMethodsA = dllMethodsByEntrypointA.ToDictionary(x => x.Value, x => x.Key);
		var entrypointByDllMethodsB = dllMethodsByEntrypointB.ToDictionary(x => x.Value, x => x.Key);

		// Match class_246 methods by which dll method on class_247 they call
		var wrapperMethodsByCalledEntrypointA = new Dictionary<string, MethodInstance>();
		var wrapperMethodsByCalledEntrypointB = new Dictionary<string, MethodInstance>();

		foreach (var method in class246A.methodsOrdered) {
			var called247Methods = method.refsOut.Where(calledMethod => calledMethod.ContainingType == class247A && entrypointByDllMethodsA.ContainsKey(calledMethod)).Select(calledMethod => entrypointByDllMethodsA[calledMethod]);
			if (called247Methods.Count() == 1) {
				wrapperMethodsByCalledEntrypointA.Add(called247Methods.Single(), method);
			}
		}

		foreach (var method in class246B.methodsOrdered) {
			var called247Methods = method.refsOut.Where(calledMethod => calledMethod.ContainingType == class247B && entrypointByDllMethodsB.ContainsKey(calledMethod)).Select(calledMethod => entrypointByDllMethodsB[calledMethod]);
			if (called247Methods.Count() == 1) {
				wrapperMethodsByCalledEntrypointB.Add(called247Methods.Single(), method);
			}
		}

		count = 0;
		foreach (var entrypoint in wrapperMethodsByCalledEntrypointA.Keys.Union(wrapperMethodsByCalledEntrypointB.Keys)) {
			if (!wrapperMethodsByCalledEntrypointA.ContainsKey(entrypoint)) {
				Console.WriteLine($"Unmatched method in assembly B: {wrapperMethodsByCalledEntrypointB[entrypoint].CecilMethod!.FullName}");
			} else if (!wrapperMethodsByCalledEntrypointB.ContainsKey(entrypoint)) {
				Console.WriteLine($"Unmatched method in assembly A: {GetIntermediaryForMethodA(wrapperMethodsByCalledEntrypointA[entrypoint])} ({wrapperMethodsByCalledEntrypointA[entrypoint].CecilMethod!.FullName})");
			} else {
				matcher.MatchMethod(
					wrapperMethodsByCalledEntrypointA[entrypoint],
					wrapperMethodsByCalledEntrypointB[entrypoint]
				);
				count++;
			}
		}
		Console.WriteLine($"Matched {count} methods on class_246");
	}

	private void MatchClass125AndClass213() {
		// These classes fail to match automatically because they're identical and have similar usages;
		// we distinguish them by class_300.field_2332 being set to a different value in class_300.MoveNext() depending on which is constructed (constructor matches .ctor(CampaignItem))
		const string class300Intermediary = "class_300";
		const string field2332Intermediary = "field_2332";
		// const string field2342Intermediary = "field_2342";
		var class300A = FindTypeAFromIntermediary(class300Intermediary);
		var field2332A = FindFieldAFromIntermediary(class300A, field2332Intermediary);
		// var field2342A = FindFieldAFromIntermediary(class300A, field2342Intermediary);
		
		var class300B = class300A.GetMatch();
		var field2332B = field2332A.GetMatch();
		// var field2342B = field2342A.GetMatch();

		if (class300B == null) throw new Exception("Failed to get match for class_300");
		if (field2332B == null) throw new Exception("Failed to get match for field_2332");
		// if (field2342B == null) throw new Exception("Failed to get match for field_2342");

		var getNextA = class300A.GetMethod("MoveNext", null);
		var getNextB = class300B.GetMethod("MoveNext", null);

		var constructedTypesBySetFieldValueA = new Dictionary<int, TypeReference>();
		var constructedTypesBySetFieldValueB = new Dictionary<int, TypeReference>();

		TypeReference? lastConstructedType = null;
		int? lastIntConstant = null;
		foreach (var instr in getNextA.CecilMethod.Body.Instructions) {
			var maybeIntValue = ClassifierUtil.getLdcI4Value(instr);
			if (instr.OpCode == OpCodes.Newobj) {
				var constructor = (MethodReference) instr.Operand;
				if (constructor.Parameters.Count == 1 && constructor.Parameters[0].ParameterType.Name == "CampaignItem") {
					lastConstructedType = constructor.DeclaringType;
				}
			} else if (maybeIntValue != null) {
				lastIntConstant = maybeIntValue;
			} else if (instr.OpCode == OpCodes.Stfld && ((FieldReference) instr.Operand).Name == field2332A.GetName()) {
				if (lastConstructedType != null && lastIntConstant != null) {
					constructedTypesBySetFieldValueA.Add((int) lastIntConstant, lastConstructedType);
				}
				lastConstructedType = null;
				lastIntConstant = null;
			}
		}

		lastConstructedType = null;
		lastIntConstant = null;
		foreach (var instr in getNextB.CecilMethod.Body.Instructions) {
			var maybeIntValue = ClassifierUtil.getLdcI4Value(instr);
			if (instr.OpCode == OpCodes.Newobj) {
				var constructor = (MethodReference) instr.Operand;
				if (constructor.Parameters.Count == 1 && constructor.Parameters[0].ParameterType.Name == "CampaignItem") {
					lastConstructedType = constructor.DeclaringType;
				}
			} else if (maybeIntValue != null) {
				lastIntConstant = maybeIntValue;
			} else if (instr.OpCode == OpCodes.Stfld && ((FieldReference) instr.Operand).Name == field2332B.GetName()) {
				if (lastConstructedType != null && lastIntConstant != null) {
					constructedTypesBySetFieldValueB.Add((int) lastIntConstant, lastConstructedType);
				}
				lastConstructedType = null;
				lastIntConstant = null;
			}
		}

		var count = 0;
		foreach (var intConst in constructedTypesBySetFieldValueA.Keys.Union(constructedTypesBySetFieldValueB.Keys)) {
			if (!constructedTypesBySetFieldValueA.ContainsKey(intConst)) {
				Console.WriteLine($"Unmatched type in assembly B: {constructedTypesBySetFieldValueB[intConst].FullName}");
			} else if (!constructedTypesBySetFieldValueB.ContainsKey(intConst)) {
				Console.WriteLine($"Unmatched type in assembly A: {GetIntermediaryForTypeA(constructedTypesBySetFieldValueA[intConst])} ({constructedTypesBySetFieldValueA[intConst].FullName})");
			} else {
				matcher.MatchType(
					matcher.env.EnvA.GetCreateTypeInstance(constructedTypesBySetFieldValueA[intConst]),
					matcher.env.EnvB.GetCreateTypeInstance(constructedTypesBySetFieldValueB[intConst])
				);
				count++;
			}
		}
		Console.WriteLine($"Matched {count} of class_125 and class_213");
	}

	private void MatchSteamCallbacks() {
		// Match steam callback fields by their generic parameter
		// TODO this probably wouldn't be necessary if we were handling generics in a less hacky way
		var steamA = matcher.env.EnvA.types["Steam"];
		var steamB = matcher.env.EnvB.types["Steam"];

		var steamworksCallbackA = matcher.env.EnvA.types["Steamworks.Callback`1"];
		var steamworksCallbackB = matcher.env.EnvB.types["Steamworks.Callback`1"];

		var fieldsByGenericParamA = new Dictionary<string, FieldReference>();
		var fieldsByGenericParamB = new Dictionary<string, FieldReference>();

		foreach (var field in steamA.CecilType!.Fields) {
			if (!field.FieldType.IsGenericInstance || field.FieldType.GetElementType().FullName != steamworksCallbackA.CecilTypeReference.FullName) continue;
			fieldsByGenericParamA.Add(((GenericInstanceType) field.FieldType).GenericArguments[0].FullName, field);
		}
		foreach (var field in steamB.CecilType!.Fields) {
			if (!field.FieldType.IsGenericInstance || field.FieldType.GetElementType().FullName != steamworksCallbackB.CecilTypeReference.FullName) continue;
			fieldsByGenericParamB.Add(((GenericInstanceType) field.FieldType).GenericArguments[0].FullName, field);
		}
		var count = 0;
		foreach (var genericParam in fieldsByGenericParamA.Keys.Union(fieldsByGenericParamB.Keys)) {
			if (!fieldsByGenericParamA.ContainsKey(genericParam)) {
				Console.WriteLine($"Unmatched field in assembly B: {fieldsByGenericParamB[genericParam].FullName}");
			} else if (!fieldsByGenericParamB.ContainsKey(genericParam)) {
				Console.WriteLine($"Unmatched field in assembly A: {GetIntermediaryForFieldA(fieldsByGenericParamA[genericParam])} ({fieldsByGenericParamA[genericParam].FullName})");
			} else {
				matcher.MatchField(
						steamA.GetField(fieldsByGenericParamA[genericParam].Name, fieldsByGenericParamA[genericParam].FieldType.Name)!,
						steamB.GetField(fieldsByGenericParamB[genericParam].Name, fieldsByGenericParamB[genericParam].FieldType.Name)!);
				count++;
			}
		}
		Console.WriteLine($"Matched {count} callback fields on Steam");
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

	private bool IsMaybeLambdaGeneratedClass(TypeDefinition cls) {
		return cls.IsClass && cls.IsNested && cls.IsSealed && cls.BaseType.FullName == "System.Object";
	}

	private void MatchUnmatchedLambdaGeneratedClasses() {
		// The same lambda appearing twice in the same method seems to break matching, since two lambda classes are generated
		// To handle this we match lambdas by the method they're constructed from, and the order in which they're constructed in the method.
		// If the lambda generated class has multiple methods (why??), we similarly match those by where they're called from, and the order in which they're called if called from the same method.
		// This special-case is a bit of a disaster but oh well.
		Dictionary<TypeInstance, HashSet<TypeInstance>> lambdasByContainingClassA = [];
		Dictionary<TypeInstance, HashSet<TypeInstance>> lambdasByContainingClassB = [];
		foreach (var cls in matcher.env.EnvA.types.Values) {
			if (cls.IsMatchable() && !cls.HasMatch() && cls.CecilType != null && IsMaybeLambdaGeneratedClass(cls.CecilType)) {
				var containingCls = cls.outerType!;
				if (!lambdasByContainingClassA.ContainsKey(containingCls)) {
					lambdasByContainingClassA[containingCls] = [];
				}
				lambdasByContainingClassA[containingCls].Add(cls);
			}
		}

		foreach (var cls in matcher.env.EnvB.types.Values) {
			if (cls.IsMatchable() && !cls.HasMatch() && cls.CecilType != null && IsMaybeLambdaGeneratedClass(cls.CecilType)) {
				var containingCls = cls.outerType!;
				if (!lambdasByContainingClassB.ContainsKey(containingCls)) {
					lambdasByContainingClassB[containingCls] = [];
				}
				lambdasByContainingClassB[containingCls].Add(cls);
			}
		}

		HashSet<TypeInstance> visitedContainingClassesB = [];
		HashSet<TypeInstance> skippedContainingClassesA = [];
		HashSet<TypeInstance> skippedContainingClassesB = [];

		var matchedLambdaClassCount = 0;
		var matchedLambdaMethodsCount = 0;

		foreach (var containingClsA in lambdasByContainingClassA.Keys) {
			var containingClsB = containingClsA.GetMatch();
			if (containingClsB == null || !lambdasByContainingClassB.ContainsKey(containingClsB)) {
				skippedContainingClassesA.Add(containingClsA);
				continue;
			}
			visitedContainingClassesB.Add(containingClsB);
			var lambdaClassesA = lambdasByContainingClassA[containingClsA];
			var lambdaClassesB = lambdasByContainingClassB[containingClsB];
			
			Dictionary<MethodInstance, HashSet<TypeInstance>> lambdasByConstructorCallSiteA = [];
			Dictionary<MethodInstance, HashSet<TypeInstance>> lambdasByConstructorCallSiteB = [];

			foreach (var lambda in lambdaClassesA) {
				var ctor = lambda.GetMethod(".ctor", null)!;
				if (ctor.refsIn.Count != 1) continue;
				var ctorCallSite = ctor.refsIn.Single();
				if (!lambdasByConstructorCallSiteA.ContainsKey(ctorCallSite)) {
					lambdasByConstructorCallSiteA[ctorCallSite] = [];
				}
				lambdasByConstructorCallSiteA[ctorCallSite].Add(lambda);
			}
			foreach (var lambda in lambdaClassesB) {
				var ctor = lambda.GetMethod(".ctor", null)!;
				if (ctor.refsIn.Count != 1) continue;
				var ctorCallSite = ctor.refsIn.Single();
				if (!lambdasByConstructorCallSiteB.ContainsKey(ctorCallSite)) {
					lambdasByConstructorCallSiteB[ctorCallSite] = [];
				}
				lambdasByConstructorCallSiteB[ctorCallSite].Add(lambda);
			}

			foreach (var constructorCallSiteA in lambdasByConstructorCallSiteA.Keys) {
				var constructorCallSiteB = constructorCallSiteA.GetMatch();
				if (constructorCallSiteB == null || !lambdasByConstructorCallSiteB.ContainsKey(constructorCallSiteB)) {
					continue;
				}
				var lambdasA = lambdasByConstructorCallSiteA[constructorCallSiteA];
				var lambdasB = lambdasByConstructorCallSiteB[constructorCallSiteB];
				if (lambdasA.Count != lambdasB.Count) continue;
				List<TypeInstance> lambdasAOrdered = [];
				List<TypeInstance> lambdasBOrdered = [];
				foreach (var instr in constructorCallSiteA.CecilMethod!.Body.Instructions) {
					if (instr.OpCode == OpCodes.Newobj) {
						var lambdaA = lambdasA.Where(cls => cls.GetId() == ((MethodReference) instr.Operand).DeclaringType.FullName).SingleOrDefault((TypeInstance?) null);
						if (lambdaA != null && !lambdasAOrdered.Contains(lambdaA)) {
							lambdasAOrdered.Add(lambdaA);
						}
					}
				}
				foreach (var instr in constructorCallSiteB.CecilMethod!.Body.Instructions) {
					if (instr.OpCode == OpCodes.Newobj) {
						var lambdaB = lambdasB.Where(cls => cls.GetId() == ((MethodReference) instr.Operand).DeclaringType.FullName).SingleOrDefault((TypeInstance?) null);
						if (lambdaB != null && !lambdasBOrdered.Contains(lambdaB)) {
							lambdasBOrdered.Add(lambdaB);
						}
					}
				}
				if (lambdasAOrdered.Count != lambdasA.Count || lambdasBOrdered.Count != lambdasA.Count) throw new Exception("Failed to find lambda constructor invocation");
				foreach (var (a, b) in lambdasAOrdered.Zip(lambdasBOrdered)) {
					matcher.MatchType(a, b);
					matchedLambdaClassCount += 1;
					Console.WriteLine($"Matched lambda generated class: {GetIntermediaryForTypeA(a)} ({a.CecilTypeReference.FullName}) -> {b.CecilTypeReference.FullName}");
					if (a.methodsOrdered.Count != b.methodsOrdered.Count) continue;
					if (a.methodsOrdered.Count == 2) {
						matcher.MatchMethod(a.methodsOrdered.Where(m => m.GetName() != ".ctor").Single(), b.methodsOrdered.Where(m => m.GetName() != ".ctor").Single());
						matchedLambdaMethodsCount++;
					} else {
						matchedLambdaMethodsCount += MatchClassMethodsBySingleInvocationSite(a, b);
					}
				}
			}
		}
		Console.WriteLine($"Matched {matchedLambdaClassCount} lambda generated classes and {matchedLambdaMethodsCount} lambda methods");
	}

	private void MatchAngleBracketsCInnerClassMembers() {
		// Match unmatched methods and fields on an inner class called <>c. I'm assuming it's some kind of generated class but I can't find any info on it anywhere because C# seems to have an utter dearth of documentation.
		var typesA = matcher.env.EnvA.types.Values.Where(cls => cls.CecilTypeReference.Name == "<>c" && cls.CecilTypeReference.IsNested && cls.HasMatch());
		var count = 0;
		var memberCount = 0;
		foreach (var cls in typesA) {
			if (Matcher.assumeBothOrNoneObfuscated && cls.GetMatch()!.CecilTypeReference.Name != "<>c")
				throw new Exception($"Expected <>c class to be matched with <>c class, but {cls.CecilTypeReference.FullName} was matched with {cls.GetMatch()!.CecilTypeReference.FullName}");
			var matched = MatchClassMethodsBySingleInvocationSite(cls, cls.GetMatch()!);
			matched += MatchClassFieldsBySingleReadSite(cls, cls.GetMatch()!);
			memberCount += matched;
			if (matched > 0) count++;
		}
		Console.WriteLine($"Matched {memberCount} methods/fields on {count} inner classes named <>c");
		// TODO fields
	}

	private void MatchMiscObfInnerClassMembers() {
		// These might be the same kind of generated inner class as <>c but obfuscated - unsure.
		List<string> intermediaryClassNames = ["class_143", "class_146", "class_363", "class_411"];
		foreach (var intermediary in intermediaryClassNames) {
			var clsA = FindTypeAFromIntermediary(intermediary);
			if (clsA.HasMatch()) {
				MatchClassMethodsBySingleInvocationSite(clsA, clsA.GetMatch()!);
				MatchClassFieldsBySingleReadSite(clsA, clsA.GetMatch()!);
			}
		}
		// TODO fields
	}

	private int MatchClassMethodsBySingleInvocationSite(TypeInstance clsA, TypeInstance clsB) {
		var count = MatchMethodsBySingleInvocationSite(
			clsA.methodsOrdered.Where(m => m.GetName() != ".ctor" && m.GetName() != ".cctor" && !m.HasMatch()),
			clsB.methodsOrdered.Where(m => m.GetName() != ".ctor" && m.GetName() != ".cctor" && !m.HasMatch())
		);
		if (count > 0)
			Console.WriteLine($"Matched {count} methods on {clsA.GetName()}");
		return count;
	}

	private int MatchClassFieldsBySingleReadSite(TypeInstance clsA, TypeInstance clsB) {
		var count = MatchFieldBySingleReadSite(
			clsA.fieldsOrdered.Where(f => !f.HasMatch()),
			clsB.fieldsOrdered.Where(f => !f.HasMatch())
		);
		if (count > 0)
			Console.WriteLine($"Matched {count} fields on {clsA.GetName()}");
		return count;
	}


	private int MatchMethodsBySingleInvocationSite(IEnumerable<MethodInstance> inMethodsA, IEnumerable<MethodInstance> inMethodsB) {
		// Match methods assuming that they are each called from exactly one method
		Dictionary<MethodInstance, HashSet<MethodInstance>> methodsByCallSiteA = [];
		Dictionary<MethodInstance, HashSet<MethodInstance>> methodsByCallSiteB = [];

		var matchedMethodsCount = 0;

		foreach (var method in inMethodsA) {
			var methodCallSite = method.refsIn.Single();
			if (!methodsByCallSiteA.ContainsKey(methodCallSite)) {
				methodsByCallSiteA[methodCallSite] = [];
			}
			methodsByCallSiteA[methodCallSite].Add(method);
		}
		foreach (var method in inMethodsB) {
			var methodCallSite = method.refsIn.Single();
			if (!methodsByCallSiteB.ContainsKey(methodCallSite)) {
				methodsByCallSiteB[methodCallSite] = [];
			}
			methodsByCallSiteB[methodCallSite].Add(method);
		}

		foreach (var callSiteA in methodsByCallSiteA.Keys) {
			var callSiteB = callSiteA.GetMatch();
			if (callSiteB == null || !methodsByCallSiteB.ContainsKey(callSiteB)) {
				continue;
			}
			var methodsA = methodsByCallSiteA[callSiteA];
			var methodsB = methodsByCallSiteB[callSiteB];
			if (methodsA.Count != methodsB.Count) continue;
			List<MethodInstance> methodsAOrdered = [];
			List<MethodInstance> methodsBOrdered = [];
			foreach (var instr in callSiteA.CecilMethod!.Body.Instructions) {
				if (instr.Operand is MethodReference methodReference) {
					var methodA = methodsA.Where(method => method.GetName() == methodReference.Name).SingleOrDefault((MethodInstance?) null);
					if (methodA != null && !methodsAOrdered.Contains(methodA)) {
						methodsAOrdered.Add(methodA);
					}
				}
			}
			foreach (var instr in callSiteB.CecilMethod!.Body.Instructions) {
				if (instr.Operand is MethodReference methodReference) {
					var methodB = methodsB.Where(method => method.GetName() == methodReference.Name).SingleOrDefault((MethodInstance?) null);
					if (methodB != null && !methodsBOrdered.Contains(methodB)) {
						methodsBOrdered.Add(methodB);
					}
				}
			}
			if (methodsAOrdered.Count != methodsA.Count || methodsBOrdered.Count != methodsA.Count) throw new Exception("Failed to find method invocation");
			foreach (var (a, b) in methodsAOrdered.Zip(methodsBOrdered)) {
				matcher.MatchMethod(a, b);
				matchedMethodsCount++;
			}
		}
		return matchedMethodsCount;
	}

	private int MatchFieldBySingleReadSite(IEnumerable<FieldInstance> inFieldsA, IEnumerable<FieldInstance> inFieldsB) {
		// Match fields assuming that they are each read from exactly one method that's outside of their own class
		Dictionary<MethodInstance, HashSet<FieldInstance>> fieldsByReadSiteA = [];
		Dictionary<MethodInstance, HashSet<FieldInstance>> fieldsByReadSiteB = [];

		var matchedFieldsCount = 0;

		foreach (var field in inFieldsA) {
			var fieldReadSite = field.readRefs.Single();
			if (!fieldsByReadSiteA.ContainsKey(fieldReadSite)) {
				fieldsByReadSiteA[fieldReadSite] = [];
			}
			fieldsByReadSiteA[fieldReadSite].Add(field);
		}
		foreach (var field in inFieldsB) {
			// ignore read-refs from the field's own class
			var fieldReadSite = field.readRefs.Where(method => method.ContainingType.GetId() != field.ContainingType.GetId()).Single();
			if (!fieldsByReadSiteB.ContainsKey(fieldReadSite)) {
				fieldsByReadSiteB[fieldReadSite] = [];
			}
			fieldsByReadSiteB[fieldReadSite].Add(field);
		}

		foreach (var callSiteA in fieldsByReadSiteA.Keys) {
			var callSiteB = callSiteA.GetMatch();
			if (callSiteB == null || !fieldsByReadSiteB.ContainsKey(callSiteB)) {
				continue;
			}
			var fieldsA = fieldsByReadSiteA[callSiteA];
			var fieldsB = fieldsByReadSiteB[callSiteB];
			if (fieldsA.Count != fieldsB.Count) continue;
			List<FieldInstance> fieldsAOrdered = [];
			List<FieldInstance> fieldsBOrdered = [];
			foreach (var instr in callSiteA.CecilMethod!.Body.Instructions) {
				if ((instr.OpCode == OpCodes.Ldfld || instr.OpCode == OpCodes.Ldsfld) && instr.Operand is FieldReference fieldReference) {
					var fieldA = fieldsA.Where(field => field.GetName() == fieldReference.Name).SingleOrDefault((FieldInstance?) null);
					if (fieldA != null && !fieldsAOrdered.Contains(fieldA)) {
						fieldsAOrdered.Add(fieldA);
					}
				}
			}
			foreach (var instr in callSiteB.CecilMethod!.Body.Instructions) {
				if ((instr.OpCode == OpCodes.Ldfld || instr.OpCode == OpCodes.Ldsfld) && instr.Operand is FieldReference fieldReference) {
					var fieldB = fieldsB.Where(field => field.GetName() == fieldReference.Name).SingleOrDefault((FieldInstance?) null);
					if (fieldB != null && !fieldsBOrdered.Contains(fieldB)) {
						fieldsBOrdered.Add(fieldB);
					}
				}
			}
			if (fieldsAOrdered.Count != fieldsA.Count || fieldsBOrdered.Count != fieldsA.Count) throw new Exception("Failed to find field read");
			foreach (var (a, b) in fieldsAOrdered.Zip(fieldsBOrdered)) {
				matcher.MatchField(a, b);
				matchedFieldsCount++;
			}
		}
		return matchedFieldsCount;
	}
}
