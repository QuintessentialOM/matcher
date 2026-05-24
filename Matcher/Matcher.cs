using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Matcher.Matching;
using Matcher.Matching.Classifier;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Matcher;

public class Matcher {
	public static readonly Regex NonObfuscatedPattern = new("^[a-zA-Z_\\`][a-zA-Z0-9_\\`]*(\\[])*$");

	public MatchingEnv env;
	readonly LocalClassEnv envA;
	readonly LocalClassEnv envB;

	public Matcher() {
		env = new();
		envA = env.EnvA;
		envB = env.EnvB;
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

	public void Init(ModuleDefinition moduleA, ModuleDefinition moduleB, string stringDeobfNameA, string stringDeobfNameB, List<string> stringsPathsA, List<string> stringsPathsB) {
		// TODO want to preprocess by replacing string deobf method calls with `ldstr`, like OpusMutatum does
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
			envA.types[type.Name] = new TypeInstance(envA, type, !NonObfuscatedPattern.IsMatch(type.Name));
		}
		foreach (TypeDefinition type in CollectNestedTypes(moduleB.Types)) {
			envB.types[type.Name] = new TypeInstance(envB, type, !NonObfuscatedPattern.IsMatch(type.Name));
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
		foreach (var (position, genericParam) in cls.CecilType.GenericParameters.WithIndex()) {
			var genericParamInstance = new GenericParamInstance(env, cls, genericParam, position, !NonObfuscatedPattern.IsMatch(genericParam.Name));
			cls.genericParamsById[genericParamInstance.GetId()] = genericParamInstance;
			cls.genericParamsOrdered.Add(genericParamInstance);
		}

		var parent = cls.CecilType.BaseType;
		if (parent != null) {
			var parentTypeInstance = env.GetCreateTypeInstance(parent.Name);
			parentTypeInstance.childTypes.Add(cls);
			cls.baseType = parentTypeInstance;
		}
		foreach (var nestedType in cls.CecilType.NestedTypes) {
			var nestedTypeInstance = env.GetCreateTypeInstance(nestedType.Name);
			nestedTypeInstance.outerType = cls;
			cls.nestedTypes.Add(nestedTypeInstance);
		}
		foreach (var iface in cls.CecilType.Interfaces) {
			var ifaceInstance = env.GetCreateTypeInstance(iface.InterfaceType.Name);
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
				HandleMethodInvocation(env, method, mref.DeclaringType.Name, mref.Name, null); // TODO desc
			}

			if (instr.Operand is FieldReference fref) {
				var owner = env.GetCreateTypeInstance(fref.DeclaringType.Name);
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

	private void HandleMethodInvocation(LocalClassEnv env, MethodInstance method, string rawOwner, string name, string? desc) {
		MethodInstance dst = ResolveMethod(env, rawOwner, name, desc, true)!;
		if (dst == null) return; // TODO

		dst.refsIn.Add(method);
		method.refsOut.Add(dst);
		dst.ContainingType.methodTypeRefs.Add(method);
		method.typeRefs.Add(dst.ContainingType);
	}

	private MethodInstance? ResolveMethod(LocalClassEnv env, string owner, string name, string? desc, bool create) {
		TypeInstance? cls = env.GetCreateTypeInstance(owner, create);
		if (cls == null) return null;

		MethodInstance? ret = cls.GetMethod(name, desc);

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

		// TODO generics
	}
	
	public void MatchMethod(MethodInstance a, MethodInstance b) {
		if (a == null) throw new NullReferenceException("null method A");
		if (b == null) throw new NullReferenceException("null method B");
		// if (a.getCls().getMatch() != b.getCls()) throw new IllegalArgumentException("the methods don't belong to the same class");
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
						UnmatchMethodParams(m);
						m.GetMatch()!.SetMatch(null);
						m.SetMatch(null);
					}
				}
			}

			if (b.hierarchyData?.MatchedHierarchy != null) {
				foreach (MethodInstance m in membersB!) {
					if (m.HasMatch()) {
						UnmatchMethodParams(m);
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
				UnmatchMethodParams(a);
				a.GetMatch()!.SetMatch(null);
				a.SetMatch(null);
			}

			if (b.GetMatch() != null) {
				UnmatchMethodParams(b);
				b.GetMatch()!.SetMatch(null);
				b.SetMatch(null);
			}

			a.SetMatch(b);
			b.SetMatch(a);
		}
	}

	public void MatchGenericParam(GenericParamInstance a, GenericParamInstance b) {
		if (a == null) throw new NullReferenceException("null generic param A");
		if (b == null) throw new NullReferenceException("null generic param B");
		// if (a.getCls().getMatch() != b.getCls()) throw new IllegalArgumentException("the methods don't belong to the same class");
		if (a.GetMatch() == b) return;

		// LOGGER.debug("Matching field {} => {}{}", a, b, (a.hasMappedName() ? " ("+a.getName(NameType.MAPPED_PLAIN)+")" : ""));

		if (a.GetMatch() != null) a.GetMatch()!.SetMatch(null);
		if (b.GetMatch() != null) b.GetMatch()!.SetMatch(null);

		a.SetMatch(b);
		b.SetMatch(a);
	}

	public void MatchField(FieldInstance a, FieldInstance b) {
		if (a == null) throw new NullReferenceException("null field A");
		if (b == null) throw new NullReferenceException("null field B");
		// if (a.getCls().getMatch() != b.getCls()) throw new IllegalArgumentException("the methods don't belong to the same class");
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
		// if (a.getMethod().getMatch() != b.getMethod()) throw new IllegalArgumentException("the method vars don't belong to the same method");
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
	}

	private static void UnmatchMembersAndGenerics(TypeInstance cls) {
		foreach (MethodInstance m in cls.methodsById.Values) {
			if (m.GetMatch() != null) {
				m.GetMatch()!.SetMatch(null);
				m.SetMatch(null);

				UnmatchMethodParams(m);
			}
		}

		foreach (FieldInstance m in cls.fieldsById.Values) {
			if (m.GetMatch() != null) {
				m.GetMatch()!.SetMatch(null);
				m.SetMatch(null);
			}
		}

		foreach (GenericParamInstance m in cls.genericParamsById.Values) {
			if (m.GetMatch() != null) {
				m.GetMatch()!.SetMatch(null);
				m.SetMatch(null);
			}
		}
	}

	public void UnmatchMethod(MethodInstance m) {
		if (m == null) throw new NullReferenceException("null member");
		if (m.GetMatch() == null) return;

		// LOGGER.debug("Unmatching member {} (was {}){}", m, m.getMatch(), (m.hasMappedName() ? " ("+m.getName(NameType.MAPPED_PLAIN)+")" : ""));

		UnmatchMethodParams(m);

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

	public void UnmatchGenericParam(GenericParamInstance f) {
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

	private static void UnmatchMethodParams(MethodInstance m) {
		foreach (MethodParamInstance arg in m.args) {
			if (arg.GetMatch() != null) {
				arg.GetMatch()!.SetMatch(null);
				arg.SetMatch(null);
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
	private const double relClassAutoMatchThreshold = 0.075;
	private const double absMethodAutoMatchThreshold = 0.8;
	private const double relMethodAutoMatchThreshold = 0.075;
	private const double absFieldAutoMatchThreshold = 0.8;
	private const double relFieldAutoMatchThreshold = 0.075;
	private const double absMethodArgAutoMatchThreshold = 0.8;
	private const double relMethodArgAutoMatchThreshold = 0.075;
	private const double absMethodVarAutoMatchThreshold = 0.8;
	private const double relMethodVarAutoMatchThreshold = 0.075;
	public const bool assumeBothOrNoneObfuscated = true; // <-- I *think* it's safe to assume this?


	public void AutoMatchAll(Action<double> progressReceiver) {
		Console.WriteLine($"initial {GetStatus(true)}");
		if (AutoMatchClasses(ClassifierLevel.Initial, absClassAutoMatchThreshold, relClassAutoMatchThreshold, progressReceiver)) {
			Console.WriteLine($"classes {GetStatus(true)}");
			AutoMatchClasses(ClassifierLevel.Initial, absClassAutoMatchThreshold, relClassAutoMatchThreshold, progressReceiver);
		}
		Console.WriteLine($"classes {GetStatus(true)}");

		AutoMatchLevel(ClassifierLevel.Intermediate, progressReceiver);
		Console.WriteLine($"intermediate {GetStatus(true)}");
		AutoMatchLevel(ClassifierLevel.Full, progressReceiver);
		Console.WriteLine($"full {GetStatus(true)}");
		AutoMatchLevel(ClassifierLevel.Extra, progressReceiver);
		Console.WriteLine($"extra {GetStatus(true)}");

		// bool matchedAny;

		// do {
		// 	matchedAny = autoMatchMethodArgs(ClassifierLevel.Full, absMethodArgAutoMatchThreshold, relMethodArgAutoMatchThreshold, progressReceiver);
		// 	Console.WriteLine($"args {getStatus(true)}");
		// 	// matchedAny |= autoMatchMethodVars(ClassifierLevel.Full, absMethodVarAutoMatchThreshold, relMethodVarAutoMatchThreshold, progressReceiver);
		// } while (matchedAny);
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

			matchedAny |= matchedClassesBefore = AutoMatchClasses(level, absClassAutoMatchThreshold, relClassAutoMatchThreshold, progressReceiver);
		} while (matchedAny);
	}

	public bool AutoMatchClasses(Action<double> progressReceiver) {
		return AutoMatchClasses(autoMatchMaxLevel, absClassAutoMatchThreshold, relClassAutoMatchThreshold, progressReceiver);
	}

	public bool AutoMatchClasses(ClassifierLevel level, double absThreshold, double relThreshold, Action<double> progressReceiver) {
		static bool filter(TypeInstance cls) => cls.IsReal() && (!assumeBothOrNoneObfuscated || cls.IsNameObfuscated) && !cls.HasMatch() && cls.IsMatchable();

		List<TypeInstance> classes = [.. new List<TypeInstance>(envA.types.Values).Where(filter)];

		// TypeInstance[] cmpClasses = new List<TypeInstance>(envB.types.Values).Where(filter).ToList();
		List<TypeInstance> cmpClasses = [.. new List<TypeInstance>(envB.types.Values).Where(filter)];

		double maxScore = TypeClassifier.GetMaxScore(level);
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
			List<RankResult<TypeInstance>> ranking = TypeClassifier.Rank(cls, [.. cmpClasses], level, env, maxMismatch);

			if (ClassifierUtil.CheckRank(ranking, absThreshold, relThreshold, maxScore)) {
				TypeInstance match = ranking[0].Subject;

				matches[cls] = match;
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

	delegate List<RankResult<T>> IRanker<T>(T src, T[] dsts, ClassifierLevel level, MatchingEnv env, double maxMismatch);

	// <T extends MemberInstance<T>>
	private Dictionary<T, T> MatchMembers<T>(ClassifierLevel level, double absThreshold, double relThreshold,
			Func<TypeInstance, T[]> memberGetter, IRanker<T> ranker, double maxScore,
			Action<double> progressReceiver, ref int totalUnmatched) where T : MatchableMember {
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

				List<RankResult<T>> ranking = ranker.Invoke(member, memberGetter.Invoke(cls.GetMatch()), level, env, maxMismatch);

				if (ClassifierUtil.CheckRank(ranking, absThreshold, relThreshold, maxScore)) {
					T match = ranking[0].Subject;

					ret[member] = match;
				} else {
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
			int TotalMethodArgCount, int MatchedMethodArgCount, int TotalFieldCount, int MatchedFieldCount) {}

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

		foreach (TypeInstance cls in env.EnvA.types.Values) {
			if (inputsOnly && cls.CecilType == null) continue;

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
				// }
			}

			foreach (FieldInstance field in cls.fieldsById.Values) {
				// if (field.isReal()) {
					totalFieldCount++;

					if (field.HasMatch()) matchedFieldCount++;
				// }
			}
		}

		return new MatchingStatus(totalClassCount, matchedClassCount,
				totalMethodCount, matchedMethodCount,
				totalMethodArgCount, matchedMethodArgCount,
				// totalMethodVarCount, matchedMethodVarCount,
				totalFieldCount, matchedFieldCount);
	}
}
