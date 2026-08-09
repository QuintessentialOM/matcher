using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Matcher.matching.SpecialCases;
using Matcher.Matching;
using Matcher.Matching.Classifier;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Matcher;

public class Matcher {
	// Matches normal type/member names, `<Module>`, and also `<>c`, since some generated classes are named `<>c`
	public static readonly Regex NonObfuscatedPattern = new("^(?:[a-zA-Z_\\`][a-zA-Z0-9_\\`]*(\\[])*|<>c|<Module>)$");

	public MatchingEnv env;
	readonly LocalClassEnv envA;
	readonly LocalClassEnv envB;
	readonly Mappings? mappingsA;
	readonly Dictionary<string, string>? matchHints;

	public Matcher(Mappings? mappingsA, Dictionary<string, string>? matchHints) {
		env = new();
		envA = env.EnvA;
		envB = env.EnvB;
		this.mappingsA = mappingsA;
		this.matchHints = matchHints;
	}

	public static bool IsTypeFullNameDeobfuscated(TypeReference type) {
		if (!NonObfuscatedPattern.IsMatch(type.Name)) return false;
		if (type.IsGenericInstance) {
			foreach (var generic in ((GenericInstanceType) type).GenericArguments) {
				if (!IsTypeFullNameDeobfuscated(generic)) return false;
			}
		}
		if (type.DeclaringType != null && !IsTypeFullNameDeobfuscated(type.DeclaringType)) return false;
		return true;
	}


	static Collection<TypeDefinition> CollectNestedTypes(Collection<TypeDefinition> topLevel) {
		var types = new Collection<TypeDefinition>();
		foreach(var type in topLevel)
			VisitTypes(type, types.Add);
		return types;
	}
	
	static void VisitTypes(TypeDefinition top, Action<TypeDefinition> act) {
		act(top);
		foreach(var type in top.NestedTypes)
			VisitTypes(type, act);
	}

	public void Init(ModuleDefinition moduleA, ModuleDefinition moduleB, List<string> stringsPathsA, List<string> stringsPathsB) {
		var stringDeobfNameA = FindStringDeobfMethod(moduleA.EntryPoint).Item2;
		var stringDeobfNameB = FindStringDeobfMethod(moduleB.EntryPoint).Item2;
		Console.WriteLine("Inlining strings for module A");
		InlineStrings(moduleA, stringDeobfNameA, LoadStrings(stringsPathsA));
		Console.WriteLine("Inlining strings for module B");
		InlineStrings(moduleB, stringDeobfNameB, LoadStrings(stringsPathsB));

		// TODO env init stuff - see ClassFeatureExtractor.process
		// what fabric matcher does:
		// step A: methods/fields, outer classes, super classes and interfaces, and collect strings that appear in classes
		// step B: method bodies: field accesses, method invocations, class instantiation etc
		// step C: construct method hierarchies based on class hierarchies
		// step D: determine method parent/child relations and do some field stuff idk
		// step E: assign temporary names idk

		foreach (TypeDefinition type in CollectNestedTypes(moduleA.Types)) {
			envA.GetCreateTypeInstance(type);
		}
		foreach (TypeDefinition type in CollectNestedTypes(moduleB.Types)) {
			envB.GetCreateTypeInstance(type);
		}
		// ToList to copy since we mutate the types dictionary during initialization (to add extra types not present in the ModuleDefinition itself)
		foreach (var type in envA.types.Values.ToList()) {
			InitTypeA(type, envA);
		}
		foreach (var type in envB.types.Values.ToList()) {
			InitTypeA(type, envB);
		}

		foreach (var type in envA.types.Values.ToList()) {
			InitTypeB(type, envA);
		}
		foreach (var type in envB.types.Values.ToList()) {
			InitTypeB(type, envB);
		}

		foreach (var type in envA.types.Values.ToList()) {
			InitTypeC(type);
		}
		foreach (var type in envB.types.Values.ToList()) {
			InitTypeC(type);
		}

		foreach (var type in envA.types.Values.ToList()) {
			InitTypeD(type);
		}
		foreach (var type in envB.types.Values.ToList()) {
			InitTypeD(type);
		}
		MatchUnobfuscated();
	}

	// copied from OpusMutatum
	private static Dictionary<int, string> LoadStrings(List<string> StringsPaths) {
		Dictionary<int, string> Strings = [];

		if(StringsPaths.Count > 0) {
			foreach(var path in StringsPaths) {
				if(!File.Exists(path))
					continue; // TODO warn or error?
				string[] lines = File.ReadAllLines(path);
				int lastIndex = 0;
				foreach (string line in lines) {
					string[] split = line.Split(["~,~"], StringSplitOptions.None);
					if(split.Length > 1) {
						// if we *can* split on this line, then we're definitely at the first line of a string
						try {
							lastIndex = int.Parse(split[0]);
							Strings[lastIndex] = split[1];
						} catch(FormatException) { }
					} else {
						// if this line isn't blank (or even if it is), then we're continuing a previous multi-line string, so append
						Strings[lastIndex] += "\n" + line;
					}
				}
			}
			Console.WriteLine("Loaded " + Strings.Count + " strings.");
		}
		return Strings;
	}

	private static void InlineStrings(ModuleDefinition module, string stringDeobfName, Dictionary<int, string> strings) {
		var types = CollectNestedTypes(module.Types);
		int inlined = 0;
		List<(Instruction, int)> stringsToBeInlined = [];
		foreach (var type in types) {
			foreach (var method in type.Methods) {
				if(method.Body != null && method.Body.Instructions != null) {
					foreach(var instr in method.Body.Instructions) {
						if(instr != null && instr.Operand is MethodReference mref && !mref.IsWindowsRuntimeProjection) {
							if (mref.Name.Equals(stringDeobfName) && mref.Parameters.Count == 1 && instr.Previous.OpCode == OpCodes.Ldc_I4) {
								stringsToBeInlined.Add((instr, (int)instr.Previous.Operand));
							}
						}
					}
				}
			}
		}
		foreach (var stringFunc in stringsToBeInlined) {
			if(strings.ContainsKey(stringFunc.Item2)) {
				stringFunc.Item1.Previous.OpCode = OpCodes.Nop;
				stringFunc.Item1.Previous.Operand = null;
				stringFunc.Item1.OpCode = OpCodes.Ldstr;
				stringFunc.Item1.Operand = strings[stringFunc.Item2];
				inlined++;
			} else {
				Console.WriteLine($"Missing string for {stringFunc.Item2}");
			}
		}
		Console.WriteLine($"Inlined {inlined} strings");
	}

	private void InitTypeA(TypeInstance cls, LocalClassEnv env) {
		foreach (var (position, method) in cls.CecilType.Methods.WithIndex()) {
			var methodInstance = new MethodInstance(env, cls, method, position, !NonObfuscatedPattern.IsMatch(method.Name));
			cls.methodsById[methodInstance.GetId()] = methodInstance;
			cls.methodsOrdered.Add(methodInstance);

			foreach (var (genericPosition, genericParam) in method.GenericParameters.WithIndex()) {
				if (genericParam.Type == GenericParameterType.Method && genericParam.DeclaringMethod.FullName == methodInstance.CecilMethod.FullName) {
					var genericParamInstance = env.GetCreateTypeInstance(genericParam);
					env.types[genericParam.FullName] = genericParamInstance;
					methodInstance.genericParamsOrdered.Add(genericParamInstance);
					genericParamInstance.position = genericPosition;
				}
			}

			// Collect strings in method bodies. C# sets fields in constructor + static constructor so this should account for initialized string fields too... I think?
			if(method.Body != null && method.Body.Instructions != null) {
				foreach(var instr in method.Body.Instructions) {
					if(instr != null && instr.OpCode == OpCodes.Ldstr) {
						cls.strings.Add((string) instr.Operand);
						methodInstance.strings.Add((string) instr.Operand);
					}
				}
			}
		}
		foreach (var (position, field) in cls.CecilType.Fields.WithIndex()) {
			var fieldInstance = new FieldInstance(env, cls, field, position, !NonObfuscatedPattern.IsMatch(field.Name));
			cls.fieldsById[fieldInstance.GetId()] = fieldInstance;
			cls.fieldsOrdered.Add(fieldInstance);
		}
		foreach (var (genericPosition, genericParam) in cls.CecilType.GenericParameters.WithIndex()) {
			if (genericParam.DeclaringType.FullName == cls.GetId()) {
				var genericParamInstance = env.GetCreateTypeInstance(genericParam);
				env.types[genericParam.FullName] = genericParamInstance;
				cls.genericParamsOrdered.Add(genericParamInstance);
				genericParamInstance.position = genericPosition;
			}
		}

		var parent = cls.CecilType.BaseType;
		if (parent != null) {
			var parentTypeInstance = env.GetCreateTypeInstance(parent);
			parentTypeInstance.childTypes.Add(cls);
			cls.baseType = parentTypeInstance;
		}
		foreach (var (index, nestedType) in cls.CecilType.NestedTypes.WithIndex()) {
			var nestedTypeInstance = env.GetCreateTypeInstance(nestedType);
			nestedTypeInstance.outerType = cls;
			nestedTypeInstance.position = index;
			cls.nestedTypes.Add(nestedTypeInstance);
		}
		foreach (var iface in cls.CecilType.Interfaces) {
			var ifaceInstance = env.GetCreateTypeInstance(iface.InterfaceType);
			ifaceInstance.implementedBy.Add(cls);
			cls.interfaces.Add(ifaceInstance);
		}
	}

	private void InitTypeB(TypeInstance cls, LocalClassEnv env) {
		foreach (MethodInstance method in cls.methodsOrdered) {
			ProcessMethodInsns(env, method);
		}
	}

	private void ProcessMethodInsns(LocalClassEnv env, MethodInstance method) {
		// if (!method.isReal()) { // artificial method to capture calls to types with incomplete/unknown hierarchy/super type method info
		// 	logger.debug("Skipping empty method {}", method);
		// 	return;
		// }

		if (method.CecilMethod == null || method.CecilMethod.Body == null || method.CecilMethod.Body.Instructions == null) {
			return;
		}

		foreach (var instr in method.CecilMethod.Body.Instructions) {
			if (instr == null) continue;
			// TODO does this cover all (non-dynamic) method invocations? does it include calling ctors?
			if (instr.Operand is MethodReference mref/* && !mref.IsWindowsRuntimeProjection*/) {
				HandleMethodInvocation(env, method, mref); // TODO desc
			}

			if (instr.Operand is FieldReference fref) {
				var owner = env.GetCreateTypeInstance(fref.DeclaringType);
				var fieldInstance = owner.GetField(fref.Name, fref.FieldType.Name);
				if (fieldInstance == null) {
					continue; // TODO
				}
				// TODO field reads and field address accesses are currently treated the same. probably shouldn't do that?
				if (instr.OpCode == OpCodes.Stfld || instr.OpCode == OpCodes.Stsfld) {
					fieldInstance.writeRefs.Add(method);
					method.fieldWriteRefs.Add(fieldInstance);
				} else {
					fieldInstance.readRefs.Add(method);
					method.fieldReadRefs.Add(fieldInstance);
				}
				owner.methodTypeRefs.Add(method);
				method.typeRefs.Add(owner);
			}


			// switch (ain.getType()) {
			// case AbstractInsnNode.METHOD_INSN: {
			// 	MethodInsnNode in = (MethodInsnNode) ain;
			// 	handleMethodInvocation(method,
			// 			in.owner, in.name, in.desc,
			// 			Util.isCallToInterface(in), ain.getOpcode() == Opcodes.INVOKESTATIC);
			// 	break;
			// }
			// case AbstractInsnNode.FIELD_INSN: {
			// 	FieldInsnNode in = (FieldInsnNode) ain;
			// 	ClassInstance owner = getCreateClassInstance(ClassInstance.getId(in.owner));
			// 	FieldInstance dst = owner.resolveField(in.name, in.desc);

			// 	if (dst == null) { // unknown field, create a synthetic one
			// 		dst = new FieldInstance(owner, in.name, in.desc, ain.getOpcode() == Opcodes.GETSTATIC || ain.getOpcode() == Opcodes.PUTSTATIC);
			// 		owner.addField(dst);
			// 	}

			// 	if (ain.getOpcode() == Opcodes.GETSTATIC || ain.getOpcode() == Opcodes.GETFIELD) {
			// 		dst.readRefs.add(method);
			// 		method.fieldReadRefs.add(dst);
			// 	} else {
			// 		dst.writeRefs.add(method);
			// 		method.fieldWriteRefs.add(dst);
			// 	}

			// 	dst.cls.methodTypeRefs.add(method);
			// 	method.classRefs.add(dst.cls);

			// 	break;
			// }
			// case AbstractInsnNode.TYPE_INSN: {
			// 	TypeInsnNode tin = (TypeInsnNode) ain;
			// 	ClassInstance dst = getCreateClassInstance(ClassInstance.getId(tin.desc));

			// 	dst.methodTypeRefs.add(method);
			// 	method.classRefs.add(dst);

			// 	break;
			// }
			// case AbstractInsnNode.INVOKE_DYNAMIC_INSN: {
			// 	InvokeDynamicInsnNode in = (InvokeDynamicInsnNode) ain;
			// 	Handle impl = Util.getTargetHandle(in.bsm, in.bsmArgs);
			// 	if (impl == null) break;

			// 	switch (impl.getTag()) {
			// 	case Opcodes.H_INVOKEVIRTUAL:
			// 	case Opcodes.H_INVOKESTATIC:
			// 	case Opcodes.H_INVOKESPECIAL:
			// 	case Opcodes.H_NEWINVOKESPECIAL:
			// 	case Opcodes.H_INVOKEINTERFACE:
			// 		handleMethodInvocation(method,
			// 				impl.getOwner(), impl.getName(), impl.getDesc(),
			// 				Util.isCallToInterface(impl), impl.getTag() == Opcodes.H_INVOKESTATIC);
			// 		break;
			// 	default:
			// 		logger.warn("Unexpected impl tag: {}", impl.getTag());
			// 	}

			// 	break;
			// }
			// }
		}
	}

	private void HandleMethodInvocation(LocalClassEnv env, MethodInstance method, MethodReference invokedMethod) {
		MethodInstance dst = ResolveMethod(env, invokedMethod, true)!;
		if (dst == null) return; // TODO

		dst.refsIn.Add(method);
		method.refsOut.Add(dst);
		dst.ContainingType.methodTypeRefs.Add(method);
		method.typeRefs.Add(dst.ContainingType);
	}

	private MethodInstance? ResolveMethod(LocalClassEnv env, MethodReference invokedMethod, bool create) {
		TypeInstance? cls = env.GetCreateTypeInstance(invokedMethod.DeclaringType, create);
		if (cls == null) return null;

		MethodInstance? ret = cls.GetMethod(invokedMethod.Name, invokedMethod.FullName);

		if (ret == null && create) {
			// TODO
			// logger.trace("Creating synthetic method {}/{}{}", owner, name, desc);

			// ret = new MethodInstance(env, cls, name, desc, isStatic);
			// cls.addMethod(ret);
		}

		return ret;
	}

	private static void InitTypeC(TypeInstance cls) {
		// assert cls.initStep == 2;
		// cls.initStep = 3;

		/* Determine which methods share the same hierarchy by grouping all methods within a
		 * bottom-up class hierarchy by id.
		 *
		 * Methods are part of the same hierarchy if:
		 * - their id matches
		 * - neither is private or static
		 * - every methods's owner is part of a set of 2+ classes/interfaces where a class or
		 *   interface exists that is assignable to them
		 * - all of these owner sets are linked by sharing a class/interface (potentially indirectly) */
		if (cls.childTypes.Count > 0 || cls.implementedBy.Count > 0) return; // visiting only classes that aren't being extended is sufficient to visit every method

		Dictionary<string, MethodInstance> methods = [];
		Queue<TypeInstance> toCheck = new();
		toCheck.Enqueue(cls);

		while (toCheck.Count > 0) {
			cls = toCheck.Dequeue();
			foreach (MethodInstance method in cls.methodsOrdered) {
				MethodInstance? prev;

				if (IsHierarchyBarrier(method)) {
					if (method.hierarchyData == null) {
						method.hierarchyData = new MethodHierarchyData();
						method.hierarchyData.members.Add(method);
					}
				} else if ((prev = methods!.GetValueOrDefault(method.GetId(), null)) != null) {
					if (method.hierarchyData == null) {
						method.hierarchyData = prev.hierarchyData;
						method.hierarchyData!.members.Add(method);
					} else if (method.hierarchyData != prev.hierarchyData) {
						foreach (MethodInstance m in prev.hierarchyData!.members) {
							method.hierarchyData.members.Add(m);
							m.hierarchyData = method.hierarchyData;
						}
					}
				} else {
					methods[method.GetId()] = method;

					if (method.hierarchyData == null) {
						method.hierarchyData = new MethodHierarchyData();
						method.hierarchyData.members.Add(method);
					}
				}

				// assert method.hierarchyData != null;
			}

			if (cls.baseType != null) toCheck.Enqueue(cls.baseType);
			cls.interfaces.ForEach(toCheck.Enqueue);
		}
	}

	private static bool IsHierarchyBarrier(MethodInstance method) {
		// TODO handle case where cecilMethod == null
		return method.CecilMethod == null || method.CecilMethod.IsStatic || method.CecilMethod.IsPrivate;
	}

	private void InitTypeD(TypeInstance cls) {
		// assert cls.initStep == 3;

		Queue<TypeInstance> toCheck = new();
		HashSet<TypeInstance> checked_ = [];
		HashSet<MethodHierarchyData> nameObfChecked = [];

		foreach (MethodInstance method in cls.methodsOrdered) {
			// assert method.hierarchyData != null;

			
			if (method.hierarchyData!.members.Count > 1) { // may have parent/child methods
				DetermineMethodRelations(method, toCheck, checked_);

				// No idea why fabric matcher tracks name obfuscation state - shouldn't this be the same for all methods in a hierarchy, since they all have the same name??
				// // update name obfuscated state if not done yet, the name is only obfuscated if it is for all hierarchy members
				// if (nameObfChecked.add(method.hierarchyData) && method.hierarchyData.nameObfuscated) {
				// 	for (MethodInstance m : method.hierarchyData.getMembers()) {
				// 		if (!m.nameObfuscatedLocal) {
				// 			method.hierarchyData.nameObfuscated = false;
				// 			break;
				// 		}
				// 	}
				// }
			}

			// determineMethodType(method);
			//Analysis.analyzeMethod(method, common);
		}
	}

	private static void DetermineMethodRelations(MethodInstance method, Queue<TypeInstance> toCheck, HashSet<TypeInstance> checked_) {
		// if (method.origName.equals("<init>") || method.origName.equals("<clinit>")) return;
		if (method.GetName() == ".ctor") return;
		if (IsHierarchyBarrier(method)) return;

		if (method.ContainingType.baseType != null) toCheck.Enqueue(method.ContainingType.baseType);
		method.ContainingType.interfaces.ForEach(toCheck.Enqueue);
		TypeInstance cls;

		while (toCheck.Count > 0) {
			cls = toCheck.Dequeue();
			if (!checked_.Add(cls)) continue;

			MethodInstance? m = cls.methodsById!.GetValueOrDefault(method.GetId(), null);

			if (m != null && !IsHierarchyBarrier(m)) { // skips over private or static methods
				method.parents.Add(m);
				m.children.Add(method);
			} else {
				if (cls.baseType != null) toCheck.Enqueue(cls.baseType);
				cls.interfaces.ForEach(toCheck.Enqueue);
			}
		}

		checked_.Clear();
	}

	// private void determineMethodType(MethodInstance method) {
	// 	MethodType type;

	// 	if (method.getId().startsWith("<clinit>")) {
	// 		type = MethodType.CLASS_INIT;
	// 	} else if (method.getId().startsWith("<init>")) {
	// 		type = MethodType.CONSTRUCTOR;
	// 	} else if (isLambdaMethod(method)) {
	// 		type = MethodType.LAMBDA_IMPL;
	// 	} else {
	// 		type = MethodType.OTHER;
	// 	}

	// 	method.type = type;
	// }

	private void MatchUnobfuscated() {
		foreach (var typeName in envA.types.Keys) {
			var type = envA.types[typeName];
			if (type.IsNameObfuscated) continue;
			var match = envB.types!.GetValueOrDefault(typeName, null);
			if (match != null && !match.IsNameObfuscated) {
				MatchType(type, match);
			}
		}
	}

	public void MatchType(TypeInstance a, TypeInstance b) {
		if (a == null) throw new NullReferenceException("null class A");
		if (b == null) throw new NullReferenceException("null class B");
		if (a.GetSubgroup() != b.GetSubgroup()) throw new ArgumentException("trying to match types in different subgroups");
		if (a.GetArrayDimensions() != b.GetArrayDimensions()) throw new ArgumentException("the classes don't have the same amount of array dimensions");
		if (a.GetMatch() == b) return;

		// TODO logger
		// LOGGER.debug("Matching class {} => {}{}", a, b, (a.hasMappedName() ? " ("+a.getName(NameType.MAPPED_PLAIN)+")" : ""));

		if (a.GetMatch() != null) {
			a.GetMatch()!.SetMatch(null);
			UnmatchMembersAndGenerics(a);
		}

		if (b.GetMatch() != null) {
			b.GetMatch()!.SetMatch(null);
			UnmatchMembersAndGenerics(b);
		}

		a.SetMatch(b);
		b.SetMatch(a);

		// match all array dimensionalities for the corresponding type
		if (a.IsArray()) {
			var elemA = a.elementType;
			if (!elemA!.HasMatch()) MatchType(elemA, b.elementType!);
		} else {
			foreach (var arrayA in a.arrays) {
				var dims = arrayA.GetArrayDimensions();

				foreach (var arrayB in b.arrays) {
					if (arrayB.HasMatch() || arrayB.GetArrayDimensions() != dims) continue;
					MatchType(arrayA, arrayB);
					break;
				}
			}
		}

		foreach (MethodInstance src in a.methodsById.Values) {
			if (!src.IsNameObfuscated) {
				MethodInstance? dst = b.methodsById!.GetValueOrDefault(src.GetId(), null);

				if ((dst != null || (dst = b.GetMethod(src.GetName(), null)) != null) && !dst.IsNameObfuscated) { // full match or name match with no alternatives
					MatchMethod(src, dst!);
					continue;
				}
			}

			MethodHierarchyData? matchedDst = src.hierarchyData?.MatchedHierarchy;
			if (matchedDst == null) continue;

			ISet<MethodInstance> dstHierarchyMembers = matchedDst!.members;
			if (dstHierarchyMembers.Count <= 1) continue;

			foreach (MethodInstance dst in b.methodsById.Values) {
				if (dstHierarchyMembers.Contains(dst)) {
					src.SetMatchable(true);
					dst.SetMatchable(true);
					MatchMethod(src, dst);
					break;
				}
			}
		}

		// match fields that are not obfuscated

		foreach (FieldInstance src in a.fieldsById.Values) {
			if (!src.IsNameObfuscated) {
				FieldInstance? dst = b.fieldsById!.GetValueOrDefault(src.GetId(), null);

				if ((dst != null || (dst = b.GetField(src.GetName(), null)) != null) && !dst.IsNameObfuscated) { // full match or name match with no alternatives
					MatchField(src, dst);
				}
			}
		}

		// TODO generics? are there any unobf generic names?
	}
	
	public void MatchMethod(MethodInstance a, MethodInstance b) {
		if (a == null) throw new NullReferenceException("null method A");
		if (b == null) throw new NullReferenceException("null method B");
		if (a.ContainingType.GetMatch() != b.ContainingType) throw new Exception("the methods don't belong to the same class");
		if (a.GetMatch() == b) return;

		// LOGGER.debug("Matching method {} => {}{}", a, b, (a.hasMappedName() ? " ("+a.getName(NameType.MAPPED_PLAIN)+")" : ""));

		ISet<MethodInstance>? membersA = a.hierarchyData?.members;
		ISet<MethodInstance>? membersB = b.hierarchyData?.members;
		// assert membersA.contains(a);
		// assert membersB.contains(b);

		if (a.hierarchyData != null && a.hierarchyData.MatchedHierarchy != b.hierarchyData) {
			if (a.hierarchyData?.MatchedHierarchy != null) {
				foreach (MethodInstance m in membersA!) {
					if (m.HasMatch()) {
						UnmatchMethodParamsAndGenerics(m);
						m.GetMatch()!.SetMatch(null);
						m.SetMatch(null);
					}
				}
			}

			if (b.hierarchyData?.MatchedHierarchy != null) {
				foreach (MethodInstance m in membersB!) {
					if (m.HasMatch()) {
						UnmatchMethodParamsAndGenerics(m);
						m.GetMatch()!.SetMatch(null);
						m.SetMatch(null);
					}
				}
			}

			// LocalClassEnv reqEnv = a.getCls().getEnv();

			if (membersA != null && membersB != null) {
				foreach (MethodInstance ca in membersA) {
					TypeInstance cls = ca.ContainingType;
					if (!cls.HasMatch()/* || cls.getEnv() != reqEnv*/) continue;

					foreach (MethodInstance cb in cls.GetMatch()!.methodsById.Values) {
						if (membersB.Contains(cb)) {
							// assert !ca.hasMatch() && !cb.hasMatch();
							ca.SetMatch(cb);
							cb.SetMatch(ca);
							break;
						}
					}
				}
			}
		} else {
			if (a.GetMatch() != null) {
				UnmatchMethodParamsAndGenerics(a);
				a.GetMatch()!.SetMatch(null);
				a.SetMatch(null);
			}

			if (b.GetMatch() != null) {
				UnmatchMethodParamsAndGenerics(b);
				b.GetMatch()!.SetMatch(null);
				b.SetMatch(null);
			}

			a.SetMatch(b);
			b.SetMatch(a);
		}
	}

	public void MatchField(FieldInstance a, FieldInstance b) {
		if (a == null) throw new NullReferenceException("null field A");
		if (b == null) throw new NullReferenceException("null field B");
		if (a.ContainingType.GetMatch() != b.ContainingType) throw new Exception("the fields don't belong to the same class");
		if (a.GetMatch() == b) return;

		// LOGGER.debug("Matching field {} => {}{}", a, b, (a.hasMappedName() ? " ("+a.getName(NameType.MAPPED_PLAIN)+")" : ""));

		if (a.GetMatch() != null) a.GetMatch()!.SetMatch(null);
		if (b.GetMatch() != null) b.GetMatch()!.SetMatch(null);

		a.SetMatch(b);
		b.SetMatch(a);
	}

	public void MatchMethodParam(MethodParamInstance a, MethodParamInstance b) {
		if (a == null) throw new NullReferenceException("null method var A");
		if (b == null) throw new NullReferenceException("null method var B");
		if (a.ContainingMethod.GetMatch() != b.ContainingMethod) throw new Exception("the method vars don't belong to the same method");
		// if (a.isArg() != b.isArg()) throw new IllegalArgumentException("the method vars are not of the same kind");
		if (a.GetMatch() == b) return;

		// LOGGER.debug("Matching method arg {} => {}{}", a, b, (a.hasMappedName() ? " ("+a.getName(NameType.MAPPED_PLAIN)+")" : ""));

		if (a.GetMatch() != null) a.GetMatch()!.SetMatch(null);
		if (b.GetMatch() != null) b.GetMatch()!.SetMatch(null);

		a.SetMatch(b);
		b.SetMatch(a);
	}

	public void UnmatchType(TypeInstance cls) {
		if (cls == null) throw new NullReferenceException("null class");
		if (cls.GetMatch() == null) return;

		// LOGGER.debug("Unmatching class {} (was {}){}", cls, cls.getMatch(), (cls.hasMappedName() ? " ("+cls.getName(NameType.MAPPED_PLAIN)+")" : ""));

		cls.GetMatch()!.SetMatch(null);
		cls.SetMatch(null);

		UnmatchMembersAndGenerics(cls);

		if (cls.IsArray()) {
			UnmatchType(cls.elementType!);
		} else {
			foreach (TypeInstance array in cls.arrays) {
				UnmatchType(array);
			}
		}

		foreach (var concreteType in cls.concreteTypes) {
			UnmatchType(concreteType);
		}
	}

	private static void UnmatchMembersAndGenerics(TypeInstance cls) {
		foreach (MethodInstance m in cls.methodsById.Values) {
			if (m.GetMatch() != null) {
				m.GetMatch()!.SetMatch(null);
				m.SetMatch(null);

				UnmatchMethodParamsAndGenerics(m);
			}
		}

		foreach (FieldInstance m in cls.fieldsById.Values) {
			if (m.GetMatch() != null) {
				m.GetMatch()!.SetMatch(null);
				m.SetMatch(null);
			}
		}

		foreach (TypeInstance p in cls.genericParamsOrdered) {
			if (p.GetMatch() != null) {
				p.GetMatch()!.SetMatch(null);
				p.SetMatch(null);
			}
		}
	}

	public void UnmatchMethod(MethodInstance m) {
		if (m == null) throw new NullReferenceException("null member");
		if (m.GetMatch() == null) return;

		// LOGGER.debug("Unmatching member {} (was {}){}", m, m.getMatch(), (m.hasMappedName() ? " ("+m.getName(NameType.MAPPED_PLAIN)+")" : ""));

		UnmatchMethodParamsAndGenerics(m);

		m.GetMatch()!.SetMatch(null);
		m.SetMatch(null);

		if (m.hierarchyData != null) {
			foreach (MethodInstance member in m.hierarchyData.members) {
				UnmatchMethod(member);
			}
		}
	}

	public void UnmatchField(FieldInstance f) {
		if (f == null) throw new NullReferenceException("null member");
		if (f.GetMatch() == null) return;

		// LOGGER.debug("Unmatching member {} (was {}){}", f, f.getMatch(), (f.hasMappedName() ? " ("+f.getName(NameType.MAPPED_PLAIN)+")" : ""));

		f.GetMatch()!.SetMatch(null);
		f.SetMatch(null);
	}

	public void UnmatchMethodParam(MethodParamInstance a) {
		if (a == null) throw new NullReferenceException("null method param");
		if (a.GetMatch() == null) return;

		// LOGGER.debug("Unmatching method var {} (was {}){}", a, a.getMatch(), (a.hasMappedName() ? " ("+a.getName(NameType.MAPPED_PLAIN)+")" : ""));

		a.GetMatch()!.SetMatch(null);
		a.SetMatch(null);
	}

	private static void UnmatchMethodParamsAndGenerics(MethodInstance m) {
		foreach (MethodParamInstance arg in m.args) {
			if (arg.GetMatch() != null) {
				arg.GetMatch()!.SetMatch(null);
				arg.SetMatch(null);
			}
		}

		foreach (TypeInstance p in m.genericParamsOrdered) {
			if (p.GetMatch() != null) {
				p.GetMatch()!.SetMatch(null);
				p.SetMatch(null);
			}
		}
	}








	// auto matching process:
	// classes at Initial, once or twice
	// loop methods/fields/classes at Intermediate until no new matches
	// loop methods/fields/classes at Full until no new matches
	// loop methods/fields/classes at Extra until no new matches
	// loop methods params/vars at Full until no new matches
	private static readonly ClassifierLevel autoMatchMaxLevel = ClassifierLevel.Extra;


	private const double absClassAutoMatchThreshold = 0.8;
	private const double relClassAutoMatchThreshold = 0.06;
	private const double absEnumAutoMatchThreshold = 0.8;
	private const double relEnumAutoMatchThreshold = 0.06;
	private const double absDelegateAutoMatchThreshold = 0.8;
	private const double relDelegateAutoMatchThreshold = 0.06;

	private const double absMethodAutoMatchThreshold = 0.8;
	private const double relMethodAutoMatchThreshold = 0.06;
	// TODO these 0.03 and below rel thresholds are probably matching too aggressively but without it some things will just fail to match even if unchanged
	private const double absFieldAutoMatchThreshold = 0.8;
	private const double relFieldAutoMatchThreshold = 0.01;
	private const double absMethodArgAutoMatchThreshold = 0.8;
	private const double relMethodArgAutoMatchThreshold = 0.03;
	private const double absMethodVarAutoMatchThreshold = 0.8;
	private const double relMethodVarAutoMatchThreshold = 0.03;
	public const bool assumeBothOrNoneObfuscated = false; // evidently not always true in general; the Editor class was unobfuscated in the old modding version but is obfuscated in newer versions

	private const double minAbsMatchThreshold = 0.6;
	private const double minRelMatchThreshold = 0.04;


	public void AutoMatchAll(Action<double> progressReceiver) {
		Console.WriteLine($"initial {GetStatus(true)}");
		if (AutoMatchClasses(ClassifierLevel.Initial, progressReceiver)) {
			Console.WriteLine($"classes {GetStatus(true)}");
			AutoMatchClasses(ClassifierLevel.Initial, progressReceiver);
		}
		Console.WriteLine($"classes {GetStatus(true)}");

		AutoMatchLevel(ClassifierLevel.Intermediate, progressReceiver);
		Console.WriteLine($"intermediate {GetStatus(true)}");
		AutoMatchLevel(ClassifierLevel.Full, progressReceiver);
		Console.WriteLine($"full {GetStatus(true)}");

		if (mappingsA != null) {
			var specialCases = new SpecialCases(this, mappingsA);
			specialCases.DoSpecialCaseMatches();
		}

		Console.WriteLine($"special-cases {GetStatus(true)}");

		AutoMatchLevel(ClassifierLevel.Extra, progressReceiver);
		Console.WriteLine($"extra {GetStatus(true)}");

		var level = ClassifierLevel.Extra;
		var absThreshold = absClassAutoMatchThreshold;
		var relThreshold = relClassAutoMatchThreshold;
		bool matchedAny;

		// while (true) {
		// 	matchedAny = AutoMatchMethods(level, absThreshold, relThreshold, progressReceiver);
		// 	matchedAny |= AutoMatchFields(level, absThreshold, relThreshold, progressReceiver);
		// 	matchedAny |= AutoMatchClasses(level, absThreshold, relThreshold, progressReceiver, TypeSubgroup.Normal);
		// 	matchedAny |= AutoMatchClasses(level, absThreshold, relThreshold, progressReceiver, TypeSubgroup.GenericInstance);
		// 	matchedAny |= AutoMatchTypeGenericParams(level, absThreshold, relThreshold, progressReceiver);
		// 	matchedAny |= AutoMatchMethodGenericParams(level, absThreshold, relThreshold, progressReceiver);
		// 	matchedAny |= AutoMatchClasses(level, absThreshold, relThreshold, progressReceiver, TypeSubgroup.Enum);
		// 	matchedAny |= AutoMatchClasses(level, absThreshold, relThreshold, progressReceiver, TypeSubgroup.Delegate);
		// 	if (matchedAny) {
		// 		Console.WriteLine($"bruteforce {GetStatus(true)}");
		// 		absThreshold = absClassAutoMatchThreshold;
		// 		relThreshold = relClassAutoMatchThreshold;
		// 	} else {
		// 		if (absThreshold == minAbsMatchThreshold && relThreshold == minRelMatchThreshold) {
		// 			break;
		// 		} else {
		// 			absThreshold = Math.Max(minAbsMatchThreshold, absThreshold * 0.9);
		// 			relThreshold = Math.Max(minRelMatchThreshold, relThreshold * 0.9);
		// 		}
		// 	}
		// }

		do {
			matchedAny = AutoMatchMethodArgs(ClassifierLevel.Full, absMethodArgAutoMatchThreshold, relMethodArgAutoMatchThreshold, progressReceiver);
			Console.WriteLine($"args {GetStatus(true)}");
			// matchedAny |= autoMatchMethodVars(ClassifierLevel.Full, absMethodVarAutoMatchThreshold, relMethodVarAutoMatchThreshold, progressReceiver);
		} while (matchedAny);
	}

	private void AutoMatchLevel(ClassifierLevel level, Action<double> progressReceiver) {
		bool matchedAny;
		bool matchedClassesBefore = true;

		do {
			matchedAny = AutoMatchMethods(level, absMethodAutoMatchThreshold, relMethodAutoMatchThreshold, progressReceiver);
			matchedAny |= AutoMatchFields(level, absFieldAutoMatchThreshold, relFieldAutoMatchThreshold, progressReceiver);

			if (!matchedAny && !matchedClassesBefore) {
				break;
			}

			matchedAny |= matchedClassesBefore = AutoMatchClasses(level, progressReceiver);
		} while (matchedAny);
	}

	public bool AutoMatchClasses(ClassifierLevel level, Action<double> progressReceiver) {
		return AutoMatchClasses(level, absClassAutoMatchThreshold, relClassAutoMatchThreshold, progressReceiver, TypeSubgroup.Normal)
			|| AutoMatchClasses(level, absClassAutoMatchThreshold, relClassAutoMatchThreshold, progressReceiver, TypeSubgroup.GenericInstance)
			|| AutoMatchTypeGenericParams(level, absClassAutoMatchThreshold, relClassAutoMatchThreshold, progressReceiver)
			|| AutoMatchMethodGenericParams(level, absClassAutoMatchThreshold, relClassAutoMatchThreshold, progressReceiver)
			|| AutoMatchClasses(level, absEnumAutoMatchThreshold, relEnumAutoMatchThreshold, progressReceiver, TypeSubgroup.Enum)
			|| AutoMatchClasses(level, absDelegateAutoMatchThreshold, relDelegateAutoMatchThreshold, progressReceiver, TypeSubgroup.Delegate);
	}

	public bool AutoMatchClasses(ClassifierLevel level, double absThreshold, double relThreshold, Action<double> progressReceiver, TypeSubgroup subgroup) {
		bool filter(TypeInstance cls) => cls.IsReal() && (!assumeBothOrNoneObfuscated || cls.IsNameObfuscated) && !cls.HasMatch() && cls.IsMatchable() && cls.GetSubgroup() == subgroup;

		List<TypeInstance> classes = [.. new List<TypeInstance>(envA.types.Values).Where(filter)];

		// TypeInstance[] cmpClasses = new List<TypeInstance>(envB.types.Values).Where(filter).ToList();
		List<TypeInstance> cmpClasses = [.. new List<TypeInstance>(envB.types.Values).Where(filter)];

		double maxScore = TypeClassifier.GetMaxScore(level, subgroup);
		double maxMismatch = maxScore - ClassifierUtil.GetRawScore(absThreshold * (1 - relThreshold), maxScore);
		Dictionary<TypeInstance, TypeInstance> matches = [];//new ConcurrentHashDictionary<>(classes.Count);

		// runInParallel(classes, cls => {
		// 	List<RankResult<TypeInstance>> ranking = TypeClassifier.rank(cls, cmpClasses, level, env, maxMismatch);

		// 	if (ClassifierUtil.checkRank(ranking, absThreshold, relThreshold, maxScore)) {
		// 		TypeInstance match = ranking.get(0).getSubject();

		// 		matches.put(cls, match);
		// 	}
		// }, progressReceiver);

		foreach (var cls in classes) {
			if (mappingsA != null && matchHints != null) {
				var intermediaryA = GetIntermediaryForTypeA(cls);
				if (intermediaryA != null && matchHints.ContainsKey(intermediaryA)) {
					var obfB = matchHints[intermediaryA];
					var foundMatch = false;
					foreach (var clsB in cmpClasses) {
						if (clsB.CecilTypeReference.Name == obfB) {
							foundMatch = true;
							matches[cls] = clsB;
							break;
						}
					}
					if (foundMatch) continue;
				}
			}

			List<RankResult<TypeInstance>> ranking = TypeClassifier.Rank(cls, [.. cmpClasses], level, env, maxMismatch, subgroup);

			if (ClassifierUtil.CheckRank(ranking, absThreshold, relThreshold, maxScore)) {
				TypeInstance match = ranking[0].Subject;

				matches[cls] = match;
			} else if (level == ClassifierLevel.Extra) {
				// Console.WriteLine($"matching type {cls.CecilTypeReference.FullName} ranking:");
				// Console.WriteLine(string.Join("\n", ranking.Select(res => $"{res.Score}/{maxScore} (min {ClassifierUtil.GetRawScore(absThreshold, maxScore)}) {res.Subject.CecilTypeReference.FullName}")));
				// // if (ranking.Count == 1) {
				// 	Console.WriteLine(string.Join("\n", ranking[0].Results.Select(res => res.ToString())));
				// // }
				// Console.WriteLine();
			}
		}

		SanitizeMatches(matches);

		foreach (var entry in matches) {
			MatchType(entry.Key, entry.Value);
		}

		// LOGGER.info("Auto matched {} classes ({} unmatched, {} total)", matches.Count, (classes.Count - matches.Count), envA.types.Count);

		return matches.Count != 0;
	}

	// public static void runInParallel<T, C>(List<T> workSet, Consumer<T> worker, Action<double> progressReceiver) {
	// 	if (workSet.Count == 0) return;

	// 	int itemsDone = 0; // originally AtomicInteger
	// 	int updateRate = Math.max(1, workSet.Count / 200);

	// 	try {
	// 		List<Future<Void>> futures = threadPool.invokeAll(workSet.stream().<Callable<Void>>map(workItem => () => {
	// 			worker.accept(workItem);

	// 			int cItemsDone = itemsDone.incrementAndGet();

	// 			if ((cItemsDone % updateRate) == 0) {
	// 				progressReceiver.accept((double) cItemsDone / workSet.Count);
	// 			}

	// 			return null;
	// 		}).collect(Collectors.toList()));

	// 		for (Future<Void> future : futures) {
	// 			future.get();
	// 		}
	// 	} catch (ExecutionException | InterruptedException e) {
	// 		throw new RuntimeException(e);
	// 	}
	// }

	public bool AutoMatchMethods(Action<double> progressReceiver) {
		return AutoMatchMethods(autoMatchMaxLevel, absMethodAutoMatchThreshold, relMethodAutoMatchThreshold, progressReceiver);
	}

	public bool AutoMatchMethods(ClassifierLevel level, double absThreshold, double relThreshold, Action<double> progressReceiver) {
		int totalUnmatched = 0; // originally AtomicInteger
		Dictionary<MethodInstance, MethodInstance> matches = MatchMembers(level, absThreshold, relThreshold,
				cls => cls.methodsById.Values.ToArray(), MethodClassifier.Rank, MethodClassifier.GetMaxScore(level),
				progressReceiver, ref totalUnmatched);

		foreach (var entry in matches) {
			MatchMethod(entry.Key, entry.Value);
		}

		// LOGGER.info("Auto matched {} methods ({} unmatched)", matches.Count, totalUnmatched);

		return matches.Count != 0;
	}

	public bool AutoMatchFields(Action<double> progressReceiver) {
		return AutoMatchFields(autoMatchMaxLevel, absFieldAutoMatchThreshold, relFieldAutoMatchThreshold, progressReceiver);
	}

	public bool AutoMatchFields(ClassifierLevel level, double absThreshold, double relThreshold, Action<double> progressReceiver) {
		int totalUnmatched = 0; // originally AtomicInteger
		double maxScore = FieldClassifier.GetMaxScore(level);

		Dictionary<FieldInstance, FieldInstance> matches = MatchMembers(level, absThreshold, relThreshold,
				cls => cls.fieldsById.Values.ToArray(), FieldClassifier.Rank, maxScore,
				progressReceiver, ref totalUnmatched);

		foreach (var entry in matches) {
			MatchField(entry.Key, entry.Value);
		}

		// LOGGER.info("Auto matched {} fields ({} unmatched)", matches.Count, totalUnmatched);

		return matches.Count != 0;
	}

	public bool AutoMatchTypeGenericParams(ClassifierLevel level, double absThreshold, double relThreshold, Action<double> progressReceiver) {
		int totalUnmatched = 0; // originally AtomicInteger
		double maxScore = TypeClassifier.GetMaxScore(level, TypeSubgroup.TypeGenericParameter);

		Dictionary<TypeInstance, TypeInstance> matches = MatchMembers(level, absThreshold, relThreshold,
				cls => cls.genericParamsOrdered.ToArray(), (a, b, c, d, e) => TypeClassifier.Rank(a, b, c, d, e, TypeSubgroup.TypeGenericParameter), maxScore,
				progressReceiver, ref totalUnmatched);

		foreach (var entry in matches) {
			MatchType(entry.Key, entry.Value);
		}

		return matches.Count != 0;
	}

	delegate List<RankResult<T>> IRanker<T>(T src, T[] dsts, ClassifierLevel level, MatchingEnv env, double maxMismatch);

	private Dictionary<T, T> MatchMembers<T>(ClassifierLevel level, double absThreshold, double relThreshold,
			Func<TypeInstance, T[]> memberGetter, IRanker<T> ranker, double maxScore,
			Action<double> progressReceiver, ref int totalUnmatched) where T : Matchable {
		List<TypeInstance> classes = env.EnvA.types.Values
				.Where(cls => /*cls.isReal() &&*/ cls.HasMatch() && memberGetter.Invoke(cls).Length > 0)
				.Where(cls => {
					foreach (T member in memberGetter.Invoke(cls)) {
						if (!member.HasMatch() && member.IsMatchable()) return true;
					}

					return false;
				})
				.ToList();
		if (classes.Count == 0) return [];

		double maxMismatch = maxScore - ClassifierUtil.GetRawScore(absThreshold * (1 - relThreshold), maxScore);
		Dictionary<T, T> ret = [];//new ConcurrentHashDictionary<>(512);

		// runInParallel(classes, cls => {
		// 	int unmatched = 0;

		// 	foreach (T member in memberGetter.apply(cls)) {
		// 		if (member.hasMatch() || !member.isMatchable()) continue;

		// 		List<RankResult<T>> ranking = ranker.rank(member, memberGetter.apply(cls.getMatch()), level, env, maxMismatch);

		// 		if (ClassifierUtil.checkRank(ranking, absThreshold, relThreshold, maxScore)) {
		// 			T match = ranking.get(0).getSubject();

		// 			ret.put(member, match);
		// 		} else {
		// 			unmatched++;
		// 		}
		// 	}

		// 	if (unmatched > 0) totalUnmatched.addAndGet(unmatched);
		// }, progressReceiver);

		foreach (var cls in classes) {
			int unmatched = 0;

			foreach (T member in memberGetter.Invoke(cls)) {
				if (member.HasMatch() || !member.IsMatchable()) continue;

				if (mappingsA != null && matchHints != null) {
					string? intermediaryA;
					if (member is FieldInstance field) {
						intermediaryA = GetIntermediaryForFieldA(field);
					} else if (member is MethodInstance method) {
						intermediaryA = GetIntermediaryForMethodA(method);
					} else { // TODO use match hints for type generic params
						intermediaryA = null;
					}
					if (intermediaryA != null && matchHints.ContainsKey(intermediaryA)) {
						var obfB = matchHints[intermediaryA];
						var foundMatch = false;
						var possibleMatches = memberGetter.Invoke(cls.GetMatch());
						foreach (var memberB in possibleMatches) {
							if (((MatchableMember) (object) memberB).GetName() == obfB) {
								foundMatch = true;
								ret[member] = memberB;
								break;
							}
						}
						if (foundMatch) continue;
					}
				}

				List<RankResult<T>> ranking = ranker.Invoke(member, memberGetter.Invoke(cls.GetMatch()), level, env, maxMismatch);

				if (ClassifierUtil.CheckRank(ranking, absThreshold, relThreshold, maxScore)) {
					T match = ranking[0].Subject;

					ret[member] = match;
				} else {
					// if (level == ClassifierLevel.Extra) {
					// 	Console.WriteLine($"matching member {((MatchableMember) (object) member).CecilMemberReference.FullName} ranking:");
					// 	Console.WriteLine(string.Join("\n", ranking.Select(res => $"{res.Score}/{maxScore} (min {ClassifierUtil.GetRawScore(absThreshold, maxScore)}) { ((MatchableMember) (object) res.Subject).CecilMemberReference.FullName}")));
					// 	// if (ranking.Count == 1) {
					// 		Console.WriteLine(string.Join("\n", ranking[0].Results.Select(res => res.ToString())));
					// 	// }
					// 	Console.WriteLine();
					// }
					unmatched++;
				}
			}

			// if we parallelize again
			// if (unmatched > 0) Interlocked.Add(ref totalUnmatched, unmatched);
			if (unmatched > 0) totalUnmatched += unmatched;
		}

		SanitizeMatches(ret);

		return ret;
	}

	public bool AutoMatchMethodArgs(Action<double> progressReceiver) {
		return AutoMatchMethodArgs(autoMatchMaxLevel, absMethodArgAutoMatchThreshold, relMethodArgAutoMatchThreshold, progressReceiver);
	}

	public bool AutoMatchMethodArgs(ClassifierLevel level, double absThreshold, double relThreshold, Action<double> progressReceiver) {
		return AutoMatchMethodVars(true, methodInstance => methodInstance.args, level, absThreshold, relThreshold, progressReceiver);
	}

	// public bool autoMatchMethodVars(Action<double> progressReceiver) {
	// 	return autoMatchMethodVars(autoMatchMaxLevel, absMethodVarAutoMatchThreshold, relMethodVarAutoMatchThreshold, progressReceiver);
	// }

	// public bool autoMatchMethodVars(ClassifierLevel level, double absThreshold, double relThreshold, Action<double> progressReceiver) {
	// 	return autoMatchMethodVars(false, MethodInstance.getVars, level, absThreshold, relThreshold, progressReceiver);
	// }

	public bool AutoMatchMethodGenericParams(ClassifierLevel level, double absThreshold, double relThreshold, Action<double> progressReceiver) {
		List<MethodInstance> methods = env.EnvA.types.Values
				.Where(cls => /*cls.isReal() &&*/ cls.HasMatch() && cls.methodsById.Count > 0)
				.SelectMany(cls => cls.methodsById.Values)
				.Where(m => m.HasMatch() && m.genericParamsOrdered.Count > 0)
				.Where(m => {
					foreach (var a in m.genericParamsOrdered) {
						if (!a.HasMatch() && a.IsMatchable()) return true;
					}

					return false;
				})
				.ToList();
		Dictionary<TypeInstance, TypeInstance> matches;
		int totalUnmatched = 0; // originally AtomicInteger

		if (methods.Count == 0) {
			matches = [];
		} else {
			double maxScore = MethodParamClassifier.GetMaxScore(level);
			double maxMismatch = maxScore - ClassifierUtil.GetRawScore(absThreshold * (1 - relThreshold), maxScore);
			matches = [];

			foreach (var m in methods) {
				int unmatched = 0;

				foreach (TypeInstance var in m.genericParamsOrdered) {
					if (var.HasMatch() || !var.IsMatchable()) continue;

					// TODO use match hints for method generic params

					List<RankResult<TypeInstance>> ranking = TypeClassifier.Rank(var, m.GetMatch().genericParamsOrdered.ToArray(), level, env, maxMismatch, TypeSubgroup.MethodGenericParameter);

					if (ClassifierUtil.CheckRank(ranking, absThreshold, relThreshold, maxScore)) {
						TypeInstance match = ranking[0].Subject;

						matches[var] = match;
					} else {
						unmatched++;
					}
				}

				// if we parallelize again
				// if (unmatched > 0) Interlocked.Add(ref totalUnmatched, unmatched);
				if (unmatched > 0) totalUnmatched += unmatched;
			}

			SanitizeMatches(matches);
		}

		foreach (var entry in matches) {
			MatchType(entry.Key, entry.Value);
		}

		return matches.Count != 0;
	}

	private bool AutoMatchMethodVars(bool isArg, Func<MethodInstance, MethodParamInstance[]> supplier,
			ClassifierLevel level, double absThreshold, double relThreshold, Action<double> progressReceiver) {
		List<MethodInstance> methods = env.EnvA.types.Values
				.Where(cls => /*cls.isReal() &&*/ cls.HasMatch() && cls.methodsById.Count > 0)
				.SelectMany(cls => cls.methodsById.Values)
				.Where(m => m.HasMatch() && supplier.Invoke(m).Length > 0)
				.Where(m => {
					foreach (MethodParamInstance a in supplier.Invoke(m)) {
						if (!a.HasMatch() && a.IsMatchable()) return true;
					}

					return false;
				})
				.ToList();
		Dictionary<MethodParamInstance, MethodParamInstance> matches;
		int totalUnmatched = 0; // originally AtomicInteger

		if (methods.Count == 0) {
			matches = [];
		} else {
			double maxScore = MethodParamClassifier.GetMaxScore(level);
			double maxMismatch = maxScore - ClassifierUtil.GetRawScore(absThreshold * (1 - relThreshold), maxScore);
			matches = [];//new ConcurrentHashDictionary<>(512);

			// runInParallel(methods, m => {
			// 	int unmatched = 0;

			// 	foreach (MethodVarInstance var in supplier.apply(m)) {
			// 		if (var.hasMatch() || !var.isMatchable()) continue;

			// 		List<RankResult<MethodVarInstance>> ranking = MethodVarClassifier.rank(var, supplier.apply(m.getMatch()), level, env, maxMismatch);

			// 		if (ClassifierUtil.checkRank(ranking, absThreshold, relThreshold, maxScore)) {
			// 			MethodVarInstance match = ranking.get(0).getSubject();

			// 			matches.put(var, match);
			// 		} else {
			// 			unmatched++;
			// 		}
			// 	}

			// 	if (unmatched > 0) totalUnmatched.addAndGet(unmatched);
			// }, progressReceiver);

			foreach (var m in methods) {
				int unmatched = 0;

				foreach (MethodParamInstance var in supplier.Invoke(m)) {
					if (var.HasMatch() || !var.IsMatchable()) continue;

					if (mappingsA != null && matchHints != null) {
						var intermediaryA = GetIntermediaryForMethodParamA(var, m);
						if (intermediaryA != null && matchHints.ContainsKey(intermediaryA)) {
							var obfB = matchHints[intermediaryA];
							var foundMatch = false;
							var possibleMatches = supplier.Invoke(m.GetMatch());
							foreach (var clsB in possibleMatches) {
								if (clsB.CecilParameter.Name == obfB) {
									foundMatch = true;
									matches[var] = clsB;
									break;
								}
							}
							if (foundMatch) continue;
						}
					}

					List<RankResult<MethodParamInstance>> ranking = MethodParamClassifier.Rank(var, supplier.Invoke(m.GetMatch()), level, env, maxMismatch);

					if (ClassifierUtil.CheckRank(ranking, absThreshold, relThreshold, maxScore)) {
						MethodParamInstance match = ranking[0].Subject;

						matches[var] = match;
					} else {
						unmatched++;
					}
				}

				// if we parallelize again
				// if (unmatched > 0) Interlocked.Add(ref totalUnmatched, unmatched);
				if (unmatched > 0) totalUnmatched += unmatched;
			}

			SanitizeMatches(matches);
		}

		foreach (var entry in matches) {
			MatchMethodParam(entry.Key, entry.Value);
		}

		// LOGGER.info("Auto matched {} method {}s ({} unmatched)", matches.Count, (isArg ? "arg" : "var"), totalUnmatched);

		return matches.Count != 0;
	}

	public static void SanitizeMatches<T>(Dictionary<T, T> matches) where T : Matchable {
		HashSet<T> matched = new(new IdentityEqualityComparer<T>());
		HashSet<T> conflictingMatches = new(new IdentityEqualityComparer<T>());

		foreach (T cls in matches.Values) {
			if (!matched.Add(cls)) {
				conflictingMatches.Add(cls);
			}
		}

		if (conflictingMatches.Count != 0) {
			foreach (var entry in matches.Where(entry => conflictingMatches.Contains(entry.Value)).ToList()) {
				matches.Remove(entry.Key);
			}
		}
	}

	public record MatchingStatus(int TotalClassCount, int MatchedClassCount, int TotalMethodCount, int MatchedMethodCount,
			int TotalMethodArgCount, int MatchedMethodArgCount, int TotalFieldCount, int MatchedFieldCount, int TotalGenericParamsCount, int MatchedGenericParamsCount) {}

	public MatchingStatus GetStatus(bool inputsOnly) {
		int totalClassCount = 0;
		int matchedClassCount = 0;
		int totalMethodCount = 0;
		int matchedMethodCount = 0;
		int totalMethodArgCount = 0;
		int matchedMethodArgCount = 0;
		// int totalMethodVarCount = 0;
		// int matchedMethodVarCount = 0;
		int totalFieldCount = 0;
		int matchedFieldCount = 0;
		int totalGenericParamsCount = 0;
		int matchedGenericParamsCount = 0;

		foreach (TypeInstance cls in env.EnvA.types.Values) {
			if (inputsOnly && cls.CecilType == null) continue;
			if (cls.GetSubgroup() == TypeSubgroup.GenericInstance) continue; // generic instance matching doesn't actually matter
			if (cls.IsIgnored()) continue;

			totalClassCount++;
			if (cls.HasMatch()) matchedClassCount++;

			foreach (MethodInstance method in cls.methodsById.Values) {
				// if (method.isReal()) {
					totalMethodCount++;

					if (method.HasMatch()) matchedMethodCount++;

					foreach (MethodParamInstance arg in method.args) {
						totalMethodArgCount++;

						if (arg.HasMatch()) matchedMethodArgCount++;
					}

					// foreach (MethodVarInstance var in method.getVars()) {
					// 	totalMethodVarCount++;

					// 	if (var.hasMatch()) matchedMethodVarCount++;
					// }
					foreach (var p in method.genericParamsOrdered) {
						totalGenericParamsCount++;
						if (p.HasMatch()) matchedGenericParamsCount++;
					}
				// }
			}

			foreach (FieldInstance field in cls.fieldsById.Values) {
				// if (field.isReal()) {
					totalFieldCount++;

					if (field.HasMatch()) matchedFieldCount++;
				// }
			}

			foreach (var p in cls.genericParamsOrdered) {
				totalGenericParamsCount++;
				if (p.HasMatch()) matchedGenericParamsCount++;
			}
		}

		return new MatchingStatus(totalClassCount, matchedClassCount,
				totalMethodCount, matchedMethodCount,
				totalMethodArgCount, matchedMethodArgCount,
				// totalMethodVarCount, matchedMethodVarCount,
				totalFieldCount, matchedFieldCount,
				totalGenericParamsCount, matchedGenericParamsCount);
	}

	public void LogMissingMatches(bool inputsOnly) {
		foreach (TypeInstance cls in env.EnvA.types.Values) {
			if (inputsOnly && cls.CecilType == null) continue;
			if (cls.GetSubgroup() == TypeSubgroup.GenericInstance) continue; // generic instance matching doesn't actually matter
			if (cls.IsIgnored()) continue;

			var clsMapping = mappingsA?.Classes.Where(clsMapping => clsMapping.ClassFullNameA == cls.CecilType!.FullName).SingleOrDefault((ClassMapping?) null);

			if (!cls.HasMatch()) {
				Console.WriteLine($"unmatched class {clsMapping?.ClassNameB ?? "???"} ({cls.CecilTypeReference.FullName})");
				continue;
			}

			foreach (MethodInstance method in cls.methodsById.Values) {
				var m = method.CecilMethod!;
				var methodMapping = clsMapping?.Methods.Where(methodMapping => {
					return methodMapping.MethodNameA == m.Name
						&& methodMapping.ReturnTypeFullNameA == m.ReturnType.FullName
						&& methodMapping.ArgumentTypeFullNamesA.Count == m.Parameters.Count
						&& methodMapping.ArgumentTypeFullNamesA.Zip(m.Parameters).All(pair => pair.First == pair.Second.ParameterType.FullName);
				}).SingleOrDefault((MethodMapping?) null);
				// if (method.isReal()) {
					if (!method.HasMatch()) {
						Console.WriteLine($"unmatched method {methodMapping?.MethodNameB ?? "???"} on class {clsMapping?.ClassNameB ?? "???"} ({method.CecilMethod!.FullName})");
						continue;
					}

					foreach (MethodParamInstance arg in method.args) {
						var argMapping = methodMapping?.Parameters.Where(argMapping => argMapping.ParameterNameA == arg.CecilParameter.Name).SingleOrDefault((MethodParameterMapping?) null);
						if (!arg.HasMatch()) {
							Console.WriteLine($"unmatched method param {argMapping?.ParameterNameB ?? "???"} on method {methodMapping?.MethodNameB ?? "???"} ({method.CecilMethod!.FullName} -> {arg.CecilParameter.Name})");
						}
					}
					// TODO method generic params
				// }
			}

			foreach (FieldInstance field in cls.fieldsById.Values) {
				var fieldMapping = clsMapping?.Fields.Where(fieldMapping => fieldMapping.FieldNameA == field.CecilField!.Name).SingleOrDefault((FieldMapping?) null);
				if (!field.HasMatch()) {
					Console.WriteLine($"unmatched field {fieldMapping?.FieldNameB ?? "???"} on class {clsMapping?.ClassNameB ?? "???"} ({field.CecilField!.FullName})");
				}
			}

			// TODO class generic params
		}
	}

	public static (string, string) FindStringDeobfMethod(MethodDefinition mainMethod) {
		if (mainMethod.Body != null && mainMethod.Body.Instructions != null) {
			var candidateMethods = new HashSet<(string, string)>();
			foreach (var instr in mainMethod.Body.Instructions) {
				if (instr.Operand is MethodReference methodRef && methodRef.Resolve() != null) {
					var method = methodRef.Resolve();
					// deobf method should be a static method of signature (int) => string
					if (method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == "System.Int32" && method.ReturnType.FullName == "System.String") {
						candidateMethods.Add((method.DeclaringType.Name, method.Name));
					}
				}
			}
			// fail unless we found exactly one match
			if (candidateMethods.Count == 1){
				return candidateMethods.Single();
			}
		}
		throw new Exception("Failed to find string deobf method");
	}

	public TypeInstance FindTypeAFromIntermediary(string intermediaryName) {
		var obfName = mappingsA.Classes.Where(cls => cls.ClassNameB == intermediaryName).Single().ClassFullNameA;
		return env.EnvA.types.Values.Where(type => type.CecilTypeReference.FullName == obfName).Single();
	}

	public MethodInstance FindMethodAFromIntermediary(TypeInstance typeA, string intermediaryName) {
		var obfName = mappingsA.Classes.Where(cls => cls.ClassFullNameA == typeA.CecilTypeReference.FullName).Single().Methods.Where(method => method.MethodNameB == intermediaryName).Single().MethodNameA;
		return typeA.GetMethod(obfName, null)!;
	}

	public FieldInstance FindFieldAFromIntermediary(TypeInstance typeA, string intermediaryName) {
		var obfName = mappingsA.Classes.Where(cls => cls.ClassFullNameA == typeA.CecilTypeReference.FullName).Single().Fields.Where(field => field.FieldNameB == intermediaryName).Single().FieldNameA;
		return typeA.GetField(obfName, null)!;
	}

	public string? GetIntermediaryForFieldA(FieldInstance field) => GetIntermediaryForFieldA(field.CecilField);
	// public string? GetIntermediaryForGenericA(TypeInstance type) => GetIntermediaryForGenericA(type.CecilTypeReference); // TODO
	public string? GetIntermediaryForMethodA(MethodInstance method) => GetIntermediaryForMethodA(method.CecilMethod);
	public string? GetIntermediaryForMethodParamA(MethodParamInstance param, MethodInstance method) => GetIntermediaryForMethodParamA(param.CecilParameter, method.CecilMethod);
	public string? GetIntermediaryForTypeA(TypeInstance type) => GetIntermediaryForTypeA(type.CecilTypeReference);

	public string? GetIntermediaryForFieldA(FieldReference field)
		=> FindType(field.DeclaringType)?.Fields.Where(f => f.FieldNameA == field.Name).SingleOrDefault((FieldMapping?) null)?.FieldNameB ?? field.Name;

	public string? GetIntermediaryForGenericA(GenericParameter generic)
		=> generic.Type == GenericParameterType.Method
			? FindMethod(generic.DeclaringMethod)?.GenericParameters.Where(g => g.GenericNameA == generic.Name)
				.SingleOrDefault((GenericParameterMapping?) null)?.GenericNameB ?? generic.Name
			: FindType(generic.DeclaringType)?.GenericParameters.Where(g => g.GenericNameA == generic.Name)
				.SingleOrDefault((GenericParameterMapping?) null)?.GenericNameB ?? generic.Name;

	public string? GetIntermediaryForMethodA(MethodReference method)
		=> FindMethod(method)?.MethodNameB ?? method.Name;

	public string? GetIntermediaryForMethodParamA(ParameterReference param, MethodReference method)
		=> FindMethod(method)?.Parameters.Where(p => p.ParameterNameA == param.Name).SingleOrDefault((MethodParameterMapping?) null)?.ParameterNameB ?? param.Name;

	public string? GetIntermediaryForTypeA(TypeReference type)
		=> FindType(type)?.ClassNameB ?? type.Name;

	private TypeReference GetMainType(TypeReference type) {
		if (type.IsGenericParameter)
			throw new Exception($"Attempted to get main type of generic parameter `{type.FullName}`!");

		if (type.IsGenericInstance || type.IsArray || type.IsByReference || type.IsPointer)
			return GetMainType(type.GetElementType());

		return type;
	}

	private ClassMapping? FindType(TypeReference type) {
		type = GetMainType(type); // Ignore generics, array types, reference types, etc
		return mappingsA.Classes.Where(cls => cls.ClassFullNameA == type.FullName).SingleOrDefault((ClassMapping?) null);
	}

	private MethodMapping? FindMethod(MethodReference method)
		// TODO: generic params stripped when matching method signatures due to Cecil handling generic instance method references strangely
		// probably not ideal, but maybe it's fine?
					=> FindType(method.DeclaringType)?.Methods.Where(m => {
			if (m.MethodNameA != method.Name || m.ArgumentTypeFullNamesA.Count != method.Parameters.Count || m.GenericParameters.Count != method.GenericParameters.Count)
				return false;
			var paramTypes = method.Parameters.Select(p => p.ParameterType.FullName).ToList();
			var returnType = method.ReturnType.FullName;
			// Substitute generic names so that they compare properly - method references sometimes have !!n for the n-th generic, instead of using the method definition's generic parameter name.
			foreach (var (from, to) in method.GenericParameters.Select(p => p.FullName).Zip(m.GenericParameters.Select(p => p.GenericNameA))) {
				for (int i = 0; i < paramTypes.Count; i++) {
					paramTypes[i] = paramTypes[i].Replace(from, to);
				}
				returnType = returnType.Replace(from, to);
			}
			return m.ReturnTypeFullNameA == returnType
				&& m.ArgumentTypeFullNamesA.Zip(paramTypes, (a, b) => (a, b))
					.All(pair => pair.a == pair.b);
		}).SingleOrDefault((MethodMapping?) null);
}
