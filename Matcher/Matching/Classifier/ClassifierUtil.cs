using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Matcher.Matching.Classifier;

public class ClassifierUtil {
	public static bool CheckPotentialEquality(TypeInstance a, TypeInstance b) {
		if (a == b) return true;
		if (a.GetMatch() != null) return a.GetMatch() == b;
		if (b.GetMatch() != null) return b.GetMatch() == a;
		if (!a.IsMatchable() || !b.IsMatchable()) return false;
		if (a.IsArray() != b.IsArray()) return false;
		if (a.IsArray() && !CheckPotentialEquality(a.elementType!, b.elementType!)) return false;
		if (a.GetSubgroup() != b.GetSubgroup()) return false;
		if (!CheckNameObfMatch(a, b)) return false;

		return true;
	}

	private static bool CheckNameObfMatch(Matchable a, Matchable b) {
		bool nameObfA = a.IsNameObfuscated;
		bool nameObfB = b.IsNameObfuscated;

		if (nameObfA && nameObfB) { // both obf
			return true;
		} else if (nameObfA != nameObfB) { // one obf
			return Matcher.assumeBothOrNoneObfuscated;
		} else { // neither obf
			return a.GetName().Equals(b.GetName());
		}
	}

	public static bool CheckPotentialEquality(MethodInstance a, MethodInstance b) {
		if (a == b) return true;
		if (a.GetMatch() != null) return a.GetMatch() == b;
		if (b.GetMatch() != null) return b.GetMatch() == a;
		if (!a.IsMatchable() || !b.IsMatchable()) return false;
		if (!CheckPotentialEquality(a.ContainingType, b.ContainingType)) return false;
		if (!CheckNameObfMatch(a, b)) return false;
		// if ((a.getId().StartsWith("<") || b.getId().StartsWith("<")) && !a.getName().Equals(b.getName())) return false; // require <clinit> and <init> to match

		//MethodInstance hierarchyMatch = a.getHierarchyMatch();
		//if (hierarchyMatch != null && !hierarchyMatch.getAllHierarchyMembers().contains(b)) return false;
		if ((a.HasHierarchyMatch() || b.HasHierarchyMatch()) && !a.HasMatchedHierarchy(b)) return false;

		// if (a.getType() == MethodType.LAMBDA_IMPL && b.getType() == MethodType.LAMBDA_IMPL) { // require same "out_er method" for lambdas
		// 	bool found = false;

		// 	maLoop: for (MethodInstance ma : a.getRefsIn()) {
		// 		for (MethodInstance mb : b.getRefsIn()) {
		// 			if (checkPotentialEquality(ma, mb)) {
		// 				found = true;
		// 				break maLoop;
		// 			}
		// 		}
		// 	}

		// 	if (!found) return false;
		// }

		return true;
	}

	public static bool CheckPotentialEquality(FieldInstance a, FieldInstance b) {
		if (a == b) return true;
		if (a.GetMatch() != null) return a.GetMatch() == b;
		if (b.GetMatch() != null) return b.GetMatch() == a;
		if (!a.IsMatchable() || !b.IsMatchable()) return false;
		if (!CheckPotentialEquality(a.ContainingType, b.ContainingType)) return false;
		if (!CheckNameObfMatch(a, b)) return false;

		return true;
	}

	public static bool CheckPotentialEquality(MethodParamInstance a, MethodParamInstance b) {
		if (a == b) return true;
		if (a.GetMatch() != null) return a.GetMatch() == b;
		if (b.GetMatch() != null) return b.GetMatch() == a;
		if (!a.IsMatchable() || !b.IsMatchable()) return false;
		// if (a.isArg() != b.isArg()) return false;
		if (!CheckPotentialEquality(a.ContainingMethod, b.ContainingMethod)) return false;
		if (!CheckNameObfMatch(a, b)) return false;

		return true;
	}

	public static bool CheckPotentialEqualityNullable(TypeInstance? a, TypeInstance? b) {
		if (a == null && b == null) return true;
		if (a == null || b == null) return false;

		return CheckPotentialEquality(a, b);
	}

	public static bool CheckPotentialEqualityNullable(MethodInstance? a, MethodInstance? b) {
		if (a == null && b == null) return true;
		if (a == null || b == null) return false;

		return CheckPotentialEquality(a, b);
	}

	public static bool CheckPotentialEqualityNullable(FieldInstance? a, FieldInstance? b) {
		if (a == null && b == null) return true;
		if (a == null || b == null) return false;

		return CheckPotentialEquality(a, b);
	}

	public static bool CheckPotentialEqualityNullable(MethodParamInstance? a, MethodParamInstance? b) {
		if (a == null && b == null) return true;
		if (a == null || b == null) return false;

		return CheckPotentialEquality(a, b);
	}

	public static double CompareCounts(int countA, int countB) {
		int delta = Math.Abs(countA - countB);
		if (delta == 0) return 1;

		return 1 - (double) delta / Math.Max(countA, countB);
	}

	public static double CompareSets<T>(ISet<T> setA, ISet<T> setB, bool readOnly) {
		if (readOnly) setB = new HashSet<T>(setB);

		int oldSize = setB.Count;
		setB.ExceptWith(setA);

		int matched = oldSize - setB.Count;
		int total = setA.Count - matched + oldSize;

		return total == 0 ? 1 : (double) matched / total;
	}

	public static double CompareClassSets(List<TypeInstance> setA, List<TypeInstance> setB, bool readOnly) {
		return CompareIdentitySets(new HashSet<TypeInstance>(setA, new IdentityEqualityComparer<TypeInstance>()), new HashSet<TypeInstance>(setB, new IdentityEqualityComparer<TypeInstance>()),
				readOnly, CheckPotentialEquality);
	}

	public static double CompareClassSets(ISet<TypeInstance> setA, ISet<TypeInstance> setB, bool readOnly) {
		return CompareIdentitySets(setA, setB, readOnly, CheckPotentialEquality);
	}

	public static double CompareMethodSets(ISet<MethodInstance> setA, ISet<MethodInstance> setB, bool readOnly) {
		return CompareIdentitySets(setA, setB, readOnly, CheckPotentialEquality);
	}

	public static double CompareFieldSets(ISet<FieldInstance> setA, ISet<FieldInstance> setB, bool readOnly) {
		return CompareIdentitySets(setA, setB, readOnly, CheckPotentialEquality);
	}

	private static double CompareIdentitySets<T>(ISet<T> setA, ISet<T> setB, bool readOnly, Func<T, T, bool> comparator) where T : Matchable {
		if (setA.Count == 0 || setB.Count == 0) {
			return setA.Count == 0 && setB.Count == 0 ? 1 : 0;
		}

		if (readOnly) {
			setA = new HashSet<T>(setA, new IdentityEqualityComparer<T>());
			setB = new HashSet<T>(setB, new IdentityEqualityComparer<T>());
		}

		int total = setA.Count + setB.Count;
		bool assumeBothOrNoneObfuscated = Matcher.assumeBothOrNoneObfuscated;//setA.GetEnumerator().next().env.sharedEnv.assumeBothOrNoneObfuscated;
		int unmatched = 0;

		// kind of messy since the original matcher code mutates the sets while enumerating

		// precise matches, nameObfuscated a
		{
			var toRemove = new HashSet<T>(new IdentityEqualityComparer<T>());
			foreach (var a in setA) {
				if (setB.Remove(a)) {
					toRemove.Add(a);
				} else if (a.GetMatch() != null) {
					if (!setB.Remove((T) a.GetMatch()!)) {
						unmatched++;
					}

					toRemove.Add(a);
				} else if (assumeBothOrNoneObfuscated && !a.IsNameObfuscated) {
					unmatched++;
					toRemove.Add(a);
				}
			}
			setA.ExceptWith(toRemove);
		}

		// nameObfuscated b
		if (assumeBothOrNoneObfuscated) {
			var toRemove = new HashSet<T>(new IdentityEqualityComparer<T>());
			foreach (var b in setB) {
				if (!b.IsNameObfuscated) {
					unmatched++;
					toRemove.Add(b);
				}
			}
			setB.ExceptWith(toRemove);
		}

		{
			var toRemove = new HashSet<T>(new IdentityEqualityComparer<T>());
			foreach (var a in setA) {
				// assert a.getMatch() == null && (!assumeBothOrNoneObfuscated || a.isNameObfuscated);
				bool found = false;

				foreach (T b in setB) {
					if (comparator.Invoke(a, b)) {
						found = true;
						break;
					}
				}

				if (!found) {
					unmatched++;
					toRemove.Add(a);
				}
			}
			setA.ExceptWith(toRemove);
		}

		foreach (T b in setB) {
			bool found = false;

			foreach (T a in setA) {
				if (comparator.Invoke(a, b)) {
					found = true;
					break;
				}
			}

			if (!found) {
				unmatched++;
			}
		}

		// assert unmatched <= total;

		return (double) (total - unmatched) / total;
	}

	public static double CompareClassLists(List<TypeInstance> listA, List<TypeInstance> listB) {
		return CompareLists(listA, listB, (list, ind) => list[ind], list => list.Count, (a, b) => CheckPotentialEquality(a, b) ? COMPARED_SIMILAR : COMPARED_DISTINCT);
	}

	public static double CompareInsns(MethodInstance a, MethodInstance b) {
		var ilA = a.CecilMethod?.Body?.Instructions;
		var ilB = b.CecilMethod?.Body?.Instructions;
		if (ilA == null || ilB == null) return 1;

		return CompareLists(ilA, ilB, (list, ind) => list[ind], list => list.Count, (inA, inB) => CompareInsns(inA, inB, ilA, ilB, (list, item) => list.IndexOf(item), a, b, a.Env.SharedEnv));
	}

	public static double CompareInsns(List<Instruction> listA, List<Instruction> listB, MatchingEnv env) {
		return CompareLists(listA, listB, (list, ind) => list[ind], list => list.Count, (inA, inB) => CompareInsns(inA, inB, listA, listB, (list, item) => list.IndexOf(item), null, null, env));
	}

	private static int CompareInsns<T>(Instruction inA, Instruction inB, T listA, T listB, Func<T, Instruction, int> posProvider,
			MethodInstance mthA, MethodInstance mthB, MatchingEnv env) {
		if (inA.Operand is MethodReference operandMethodA) {
			if (inA.OpCode != inB.OpCode || inB.Operand is not MethodReference operandMethodB) {
				return COMPARED_DISTINCT;
			} else {
				// TODO don't use null descriptors
				return CompareMethods(operandMethodA.DeclaringType, operandMethodA.Name, /*operandMethodA.desc*/ null,
					operandMethodB.DeclaringType, operandMethodB.Name, /*operandMethodB.desc*/ null,
					env) ? COMPARED_SIMILAR : COMPARED_DISTINCT;
			}
		}
		if (inA.Operand is TypeReference operandTypeA) {
			// box, isinst, constrained., initobj, newarr
			if (inA.OpCode != inB.OpCode || inB.Operand is not TypeReference operandTypeB) {
				return COMPARED_DISTINCT;
			} else {
				TypeInstance? clsA = env.EnvA.types!.GetValueOrDefault(operandTypeA.Name, null);
				TypeInstance? clsB = env.EnvB.types!.GetValueOrDefault(operandTypeB.Name, null);

				return CheckPotentialEqualityNullable(clsA, clsB) ? COMPARED_SIMILAR : COMPARED_DISTINCT;
			}
			
		}
		if (inA.Operand is FieldReference operandFieldA) {
			if (inB.Operand is not FieldReference operandFieldB) {
				return COMPARED_DISTINCT;
			}
			// TODO fabric matcher doesn't consider opcode for fields, so reads/writes are considered the same. unsure why, or if this is desirable behavior
			TypeInstance? clsA = env.EnvA.types!.GetValueOrDefault(operandFieldA.DeclaringType.Name, null);
			TypeInstance? clsB = env.EnvB.types!.GetValueOrDefault(operandFieldB.DeclaringType.Name, null);

			if (clsA == null && clsB == null) return COMPARED_SIMILAR;
			if (clsA == null || clsB == null) return COMPARED_DISTINCT;

			FieldInstance? fieldA = clsA.GetField(operandFieldA.Name, operandFieldA.DeclaringType.Name);
			FieldInstance? fieldB = clsB.GetField(operandFieldB.Name, operandFieldB.DeclaringType.Name);

			return CheckPotentialEqualityNullable(fieldA, fieldB) ? COMPARED_SIMILAR : COMPARED_DISTINCT;
		}
		if (inA.Operand is ParameterDefinition || inA.OpCode == OpCodes.Ldarg_0 || inA.OpCode == OpCodes.Ldarg_1 || inA.OpCode == OpCodes.Ldarg_2 || inA.OpCode == OpCodes.Ldarg_3) {
			var operandParamA = inA.Operand is ParameterDefinition p ? p : null;
			var indexA = operandParamA != null ? operandParamA.Index :
					inA.OpCode == OpCodes.Ldarg_0 ? 0 :
					inA.OpCode == OpCodes.Ldarg_1 ? 1 :
					inA.OpCode == OpCodes.Ldarg_2 ? 2 :
					inA.OpCode == OpCodes.Ldarg_3 ? 3 : -1;
			if (indexA == -1) {
				Console.WriteLine("param index not found; this shouldn't happen");
				return COMPARED_DISTINCT;
			}
			if (inB.Operand is ParameterDefinition || inB.OpCode == OpCodes.Ldarg_0 || inB.OpCode == OpCodes.Ldarg_1 || inB.OpCode == OpCodes.Ldarg_2 || inB.OpCode == OpCodes.Ldarg_3) {
				var operandParamB = inB.Operand is ParameterDefinition p2 ? p2 : null;
				var indexB = operandParamB != null ? operandParamB.Index :
						inB.OpCode == OpCodes.Ldarg_0 ? 0 :
						inB.OpCode == OpCodes.Ldarg_1 ? 1 :
						inB.OpCode == OpCodes.Ldarg_2 ? 2 :
						inB.OpCode == OpCodes.Ldarg_3 ? 3 : -1;
				if (indexB == -1) {
					Console.WriteLine("param index not found; this shouldn't happen");
					return COMPARED_DISTINCT;
				}
				if ((inA.OpCode == OpCodes.Ldarga || inA.OpCode == OpCodes.Ldarga_S) != (inB.OpCode == OpCodes.Ldarga || inB.OpCode == OpCodes.Ldarga_S)) {
					return COMPARED_DISTINCT; // one is loading address, the other is loading value
				}
				// Special-case `this` parameter for non-static methods
				if (!mthA.CecilMethod!.IsStatic) indexA -= 1;
				if (!mthB.CecilMethod!.IsStatic) indexB -= 1;
				if (indexA == -1 || indexB == -1) {
					return indexA == -1 && indexB == -1 ? COMPARED_SIMILAR : COMPARED_DISTINCT;
				}
				var argA = mthA.args[indexA];
				var argB = mthB.args[indexB];
				if (!CheckPotentialEquality(argA, argB)) {
					return COMPARED_DISTINCT;
				} else {
					return CheckPotentialEquality(argA.paramType, argB.paramType) ? COMPARED_SIMILAR : COMPARED_POSSIBLE;
				}
			}
			return COMPARED_DISTINCT;
		}

		// TODO probably want more special-cases for instructions that can be compared more precisely than just by opcode
		if (inA.OpCode != inB.OpCode) return COMPARED_DISTINCT;
		return COMPARED_SIMILAR;
		
		// switch (insnA.getType()) {
		// case Instruction.INT_INSN: {
		// 	IntInsnNode a = (IntInsnNode) insnA;
		// 	IntInsnNode b = (IntInsnNode) insnB;

		// 	return a.operand == b.operand ? COMPARED_SIMILAR : COMPARED_DISTINCT;
		// }
		// case Instruction.VAR_INSN: {
		// 	VarInsnNode a = (VarInsnNode) insnA;
		// 	VarInsnNode b = (VarInsnNode) insnB;

		// 	if (mthA != null && mthB != null) {
		// 		MethodParamInstance varA = mthA.getArgOrVar(a.var, posProvider.Invoke(listA, insnA));
		// 		MethodParamInstance varB = mthB.getArgOrVar(b.var, posProvider.Invoke(listB, insnB));

		// 		if (varA != null && varB != null) {
		// 			if (!checkPotentialEquality(varA, varB)) {
		// 				return COMPARED_DISTINCT;
		// 			} else {
		// 				return checkPotentialEquality(varA.getType(), varB.getType()) ? COMPARED_SIMILAR : COMPARED_POSSIBLE;
		// 			}
		// 		}
		// 	}

		// 	break;
		// }
		// case Instruction.TYPE_INSN: {
		// 	TypeInsnNode a = (TypeInsnNode) insnA;
		// 	TypeInsnNode b = (TypeInsnNode) insnB;
		// 	TypeInstance clsA = env.envA.types[a.desc];
		// 	TypeInstance clsB = env.envB.types[b.desc];

		// 	return checkPotentialEqualityNullable(clsA, clsB) ? COMPARED_SIMILAR : COMPARED_DISTINCT;
		// }
		// case Instruction.FIELD_INSN: {
		// 	FieldInsnNode a = (FieldInsnNode) insnA;
		// 	FieldInsnNode b = (FieldInsnNode) insnB;
		// 	TypeInstance clsA = env.envA.types[a.owner];
		// 	TypeInstance clsB = env.envB.types[b.owner];

		// 	if (clsA == null && clsB == null) return COMPARED_SIMILAR;
		// 	if (clsA == null || clsB == null) return COMPARED_DISTINCT;

		// 	FieldInstance fieldA = clsA.resolveField(a.name, a.desc);
		// 	FieldInstance fieldB = clsB.resolveField(b.name, b.desc);

		// 	return checkPotentialEqualityNullable(fieldA, fieldB) ? COMPARED_SIMILAR : COMPARED_DISTINCT;
		// }
		// case Instruction.METHOD_INSN: {
		// 	MethodInsnNode a = (MethodInsnNode) insnA;
		// 	MethodInsnNode b = (MethodInsnNode) insnB;

		// 	return compareMethods(a.owner, a.name, a.desc, Util.isCallToInterface(a),
		// 			b.owner, b.name, b.desc, Util.isCallToInterface(b),
		// 			env) ? COMPARED_SIMILAR : COMPARED_DISTINCT;
		// }
		// case Instruction.INVOKE_DYNAMIC_INSN: {
		// 	InvokeDynamicInsnNode a = (InvokeDynamicInsnNode) insnA;
		// 	InvokeDynamicInsnNode b = (InvokeDynamicInsnNode) insnB;

		// 	if (!a.bsm.Equals(b.bsm)) return COMPARED_DISTINCT;

		// 	if (Util.isJavaLambdaMetafactory(a.bsm)) {
		// 		Handle implA = (Handle) a.bsmArgs[1];
		// 		Handle implB = (Handle) b.bsmArgs[1];

		// 		if (implA.getTag() != implB.getTag()) return COMPARED_DISTINCT;

		// 		switch (implA.getTag()) {
		// 		case Opcodes.H_INVOKEVIRTUAL:
		// 		case Opcodes.H_INVOKESTATIC:
		// 		case Opcodes.H_INVOKESPECIAL:
		// 		case Opcodes.H_NEWINVOKESPECIAL:
		// 		case Opcodes.H_INVOKEINTERFACE:
		// 			return compareMethods(implA.getOwner(), implA.getName(), implA.getDesc(), Util.isCallToInterface(implA),
		// 					implB.getOwner(), implB.getName(), implB.getDesc(), Util.isCallToInterface(implB),
		// 					env) ? COMPARED_SIMILAR : COMPARED_DISTINCT;
		// 		default:
		// 			logger.warn("Unexpected impl tag: {}", implA.getTag());
		// 		}
		// 	} else if (!Util.isIrrelevantBsm(a.bsm)) {
		// 		logger.warn("Unknown invokedynamic bsm: {}/{}{} (tag={} iif={})",
		// 				a.bsm.getOwner(), a.bsm.getName(), a.bsm.getDesc(), a.bsm.getTag(), a.bsm.isInterface());
		// 	}

		// 	// TODO: implement
		// 	break;
		// }
		// case Instruction.JUMP_INSN: {
		// 	JumpInsnNode a = (JumpInsnNode) insnA;
		// 	JumpInsnNode b = (JumpInsnNode) insnB;

		// 	// check if the 2 jumps have the same direction
		// 	int dirA = int.signum(posProvider.Invoke(listA, a.label) - posProvider.Invoke(listA, a));
		// 	int dirB = int.signum(posProvider.Invoke(listB, b.label) - posProvider.Invoke(listB, b));

		// 	return dirA == dirB ? COMPARED_SIMILAR : COMPARED_DISTINCT;
		// }
		// case Instruction.LABEL: {
		// 	// TODO: implement
		// 	break;
		// }
		// case Instruction.LDC_INSN: {
		// 	LdcInsnNode a = (LdcInsnNode) insnA;
		// 	LdcInsnNode b = (LdcInsnNode) insnB;
		// 	Class<?> typeClsA = a.cst.getClass();

		// 	if (typeClsA != b.cst.getClass()) return COMPARED_DISTINCT;

		// 	if (typeClsA == Type.class) {
		// 		Type typeA = (Type) a.cst;
		// 		Type typeB = (Type) b.cst;

		// 		if (typeA.getSort() != typeB.getSort()) return COMPARED_DISTINCT;

		// 		switch (typeA.getSort()) {
		// 		case Type.ARRAY:
		// 		case Type.OBJECT:
		// 			return checkPotentialEqualityNullable(env.getClsByIdA(typeA.getDescriptor()), env.getClsByIdB(typeB.getDescriptor())) ? COMPARED_SIMILAR : COMPARED_DISTINCT;
		// 		case Type.METHOD:
		// 			// TODO: implement
		// 			break;
		// 		}
		// 	} else {
		// 		return a.cst.Equals(b.cst) ? COMPARED_SIMILAR : COMPARED_DISTINCT;
		// 	}

		// 	break;
		// }
		// case Instruction.IINC_INSN: {
		// 	IincInsnNode a = (IincInsnNode) insnA;
		// 	IincInsnNode b = (IincInsnNode) insnB;

		// 	if (a.incr != b.incr) return COMPARED_DISTINCT;

		// 	if (mthA != null && mthB != null) {
		// 		MethodParamInstance varA = mthA.getArgOrVar(a.var, posProvider.Invoke(listA, insnA));
		// 		MethodParamInstance varB = mthB.getArgOrVar(b.var, posProvider.Invoke(listB, insnB));

		// 		if (varA != null && varB != null) {
		// 			return checkPotentialEquality(varA, varB) ? COMPARED_SIMILAR : COMPARED_DISTINCT;
		// 		}
		// 	}

		// 	break;
		// }
		// case Instruction.TABLESWITCH_INSN: {
		// 	TableSwitchInsnNode a = (TableSwitchInsnNode) insnA;
		// 	TableSwitchInsnNode b = (TableSwitchInsnNode) insnB;

		// 	return a.Min == b.Min && a.max == b.max ? COMPARED_SIMILAR : COMPARED_DISTINCT;
		// }
		// case Instruction.LOOKUPSWITCH_INSN: {
		// 	LookupSwitchInsnNode a = (LookupSwitchInsnNode) insnA;
		// 	LookupSwitchInsnNode b = (LookupSwitchInsnNode) insnB;

		// 	return a.keys.Equals(b.keys) ? COMPARED_SIMILAR : COMPARED_DISTINCT;
		// }
		// case Instruction.MULTIANEWARRAY_INSN: {
		// 	MultiANewArrayInsnNode a = (MultiANewArrayInsnNode) insnA;
		// 	MultiANewArrayInsnNode b = (MultiANewArrayInsnNode) insnB;

		// 	if (a.dims != b.dims) return COMPARED_DISTINCT;

		// 	TypeInstance clsA = env.envA.types[a.desc];
		// 	TypeInstance clsB = env.envB.types[b.desc];

		// 	return checkPotentialEqualityNullable(clsA, clsB) ? COMPARED_SIMILAR : COMPARED_DISTINCT;
		// }
		// case Instruction.FRAME: {
		// 	// TODO: implement
		// 	break;
		// }
		// case Instruction.LINE: {
		// 	// TODO: implement
		// 	break;
		// }
		// }
	}

	private static bool CompareMethods(TypeReference ownerA, string nameA, string descA, TypeReference ownerB, string nameB, string descB, MatchingEnv env) {
		TypeInstance clsA = env.EnvA.types[ownerA.FullName];
		TypeInstance clsB = env.EnvB.types[ownerB.FullName];

		if (clsA == null && clsB == null) return true;
		if (clsA == null || clsB == null) return false;

		return CompareMethods(clsA, nameA, descA, clsB, nameB, descB);
	}

	private static bool CompareMethods(TypeInstance ownerA, string nameA, string descA, TypeInstance ownerB, string nameB, string descB) {
		MethodInstance? methodA = ownerA.GetMethod(nameA, descA);
		MethodInstance? methodB = ownerB.GetMethod(nameB, descB);

		if (methodA == null && methodB == null) return true;
		if (methodA == null || methodB == null) return false;

		return CheckPotentialEquality(methodA, methodB);
	}

	private static double CompareLists<T, U>(T listA, T listB, RetrieveListElement<T, U> elementRetriever, RetrieveListSize<T> sizeRetriever, CompareElements<U> elementComparator) {
		int sizeA = sizeRetriever.Invoke(listA);
		int sizeB = sizeRetriever.Invoke(listB);

		if (sizeA == 0 && sizeB == 0) return 1;
		if (sizeA == 0 || sizeB == 0) return 0;

		if (sizeA == sizeB) {
			bool match = true;

			for (int i = 0; i < sizeA; i++) {
				if (elementComparator.Invoke(elementRetriever.Invoke(listA, i), elementRetriever.Invoke(listB, i)) != COMPARED_SIMILAR) {
					match = false;
					break;
				}
			}

			if (match) return 1;
		}

		// levenshtein distance as per wp (https://en.wikipedia.org/wiki/Levenshtein_distance#Iterative_with_two_matrix_rows)
		int[] v0 = new int[sizeB + 1];
		int[] v1 = new int[sizeB + 1];

		for (int i = 1; i < v0.Length; i++) {
			v0[i] = i * COMPARED_DISTINCT;
		}

		for (int i = 0; i < sizeA; i++) {
			v1[0] = (i + 1) * COMPARED_DISTINCT;

			for (int j = 0; j < sizeB; j++) {
				int cost = elementComparator.Invoke(elementRetriever.Invoke(listA, i), elementRetriever.Invoke(listB, j));
				v1[j + 1] = Math.Min(Math.Min(v1[j] + COMPARED_DISTINCT, v0[j + 1] + COMPARED_DISTINCT), v0[j] + cost);
			}

			for (int j = 0; j < v0.Length; j++) {
				v0[j] = v1[j];
			}
		}

		int distance = v1[sizeB];
		int upperBound = Math.Max(sizeA, sizeB) * COMPARED_DISTINCT;
		// assert distance >= 0 && distance <= upperBound;

		return 1 - (double) distance / upperBound;
	}

	public static int[]? MapInsns(MethodInstance a, MethodInstance b) {
		var ilA = a.CecilMethod?.Body?.Instructions;
		var ilB = b.CecilMethod?.Body?.Instructions;
		if (ilA == null || ilB == null) return null;

		if (ilA.Count * ilB.Count < 1000) {
			return MapInsns(ilA, ilB, a, b, a.Env.SharedEnv);
		} else {
			if (mapInsnsCache.ContainsKey((a, b))) {
				return mapInsnsCache[(a, b)];
			} else {
				var result = MapInsns(ilA, ilB, a, b, a.Env.SharedEnv);
				mapInsnsCache[(a, b)] = result;
				return result;
			}
		}
	}

	private static readonly Dictionary<(MethodInstance, MethodInstance), int[]?> mapInsnsCache = [];

	public static int[] MapInsns(Collection<Instruction> listA, Collection<Instruction> listB, MethodInstance mthA, MethodInstance mthB, MatchingEnv env) {
		return MapLists(listA, listB, (list, ind) => list[ind], list => list.Count, (inA, inB) => CompareInsns(inA, inB, listA, listB, (list, item) => list.IndexOf(item), mthA, mthB, env));
	}

	private static int[] MapLists<T, U>(T listA, T listB, RetrieveListElement<T, U> elementRetriever, RetrieveListSize<T> sizeRetriever, CompareElements<U> elementComparator) {
		int sizeA = sizeRetriever.Invoke(listA);
		int sizeB = sizeRetriever.Invoke(listB);

		if (sizeA == 0 && sizeB == 0) return [];

		int[] ret = new int[sizeA];

		if (sizeA == 0 || sizeB == 0) {
			Array.Fill(ret, -1);

			return ret;
		}

		if (sizeA == sizeB) {
			bool match = true;

			for (int i = 0; i < sizeA; i++) {
				if (elementComparator.Invoke(elementRetriever.Invoke(listA, i), elementRetriever.Invoke(listB, i)) != COMPARED_SIMILAR) {
					match = false;
					break;
				}
			}

			if (match) {
				for (int i = 0; i < ret.Length; i++) {
					ret[i] = i;
				}

				return ret;
			}
		}

		// levenshtein distance as per wp (https://en.wikipedia.org/wiki/Levenshtein_distance#Iterative_with_two_matrix_rows)
		int size = sizeA + 1;
		int[] v = new int[size * (sizeB + 1)];

		for (int i = 1; i <= sizeA; i++) {
			v[i + 0] = i * COMPARED_DISTINCT;
		}

		for (int j = 1; j <= sizeB; j++) {
			v[0 + j * size] = j * COMPARED_DISTINCT;
		}

		for (int j = 1; j <= sizeB; j++) {
			for (int i = 1; i <= sizeA; i++) {
				int cost = elementComparator.Invoke(elementRetriever.Invoke(listA, i - 1), elementRetriever.Invoke(listB, j - 1));

				v[i + j * size] = Math.Min(Math.Min(v[i - 1 + j * size] + COMPARED_DISTINCT,
						v[i + (j - 1) * size] + COMPARED_DISTINCT),
						v[i - 1 + (j - 1) * size] + cost);
			}
		}

		/*for (int j = 0; j <= sizeB; j++) {
			for (int i = 0; i <= sizeA; i++) {
				logger.debug("%2d ", v[i + j * size]);
			}

			logger.debug("");
		}*/

		{
			int i = sizeA;
			int j = sizeB;
			//bool valid = true;

			while (i > 0 || j > 0) {
				int c = v[i + j * size];
				int delCost = i > 0 ? v[i - 1 + j * size] : int.MaxValue;
				int insCost = j > 0 ? v[i + (j - 1) * size] : int.MaxValue;
				int keepCost = j > 0 && i > 0 ? v[i - 1 + (j - 1) * size] : int.MaxValue;

				if (keepCost <= delCost && keepCost <= insCost) {
					if (c - keepCost >= COMPARED_DISTINCT) {
						// assert c - keepCost == COMPARED_DISTINCT;
						//logger.debug("{}/{} rep {} => {}", i-1, j-1, toString(elementRetriever.Invoke(listA, i - 1)), toString(elementRetriever.Invoke(listB, j - 1)));
						ret[i - 1] = -1;
					} else {
						//logger.debug("{}/{} eq {} - {}", i-1, j-1, toString(elementRetriever.Invoke(listA, i - 1)), toString(elementRetriever.Invoke(listB, j - 1)));
						ret[i - 1] = j - 1;

						/*U e = elementRetriever.Invoke(listA, i - 1);

						if (e is Instruction
								&& ((Instruction) e).OpCode != ((Instruction) elementRetriever.Invoke(listB, j - 1)).OpCode) {
							valid = false;
						}*/
					}

					i--;
					j--;
				} else if (delCost < insCost) {
					//logger.debug("{}/{} del {}", i-1, j-1, toString(elementRetriever.Invoke(listA, i - 1)));
					ret[i - 1] = -1;
					i--;
				} else {
					//logger.debug("{}/{} ins {}", i-1, j-1, toString(elementRetriever.Invoke(listB, j - 1)));
					j--;
				}
			}
		}

		/*if (!valid) {
			// assert valid;
		}*/

		return ret;
	}

	public delegate int CompareElements<T>(T a, T b);

	public const int COMPARED_SIMILAR = 0;
	public const int COMPARED_POSSIBLE = 1;
	public const int COMPARED_DISTINCT = 2;

	private static string? ToString(Object node) {
		// if (node is Instruction) {
		// 	Textifier textifier = new Textifier();
		// 	MethodVisitor visitor = new TraceMethodVisitor(textifier);

		// 	((Instruction) node).accept(visitor);

		// 	return textifier.getText()[0].toString().trim();
		// } else {
			return node.ToString();
		// }
	}

	private delegate U RetrieveListElement<T, U>(T list, int pos);

	private delegate int RetrieveListSize<T>(T list);

	public static List<RankResult<T>> Rank<T>(T src, T[] dsts, List<IClassifier<T>> classifiers, Func<T, T, bool> potentialEqualityCheck, MatchingEnv env, double maxMismatch) where T : Matchable {
		List<RankResult<T>> ret = new(dsts.Length);

		foreach (T dst in dsts) {
			RankResult<T>? result = Rank(src, dst, classifiers, potentialEqualityCheck, env, maxMismatch);
			if (result != null) ret.Add(result);
		}

		// negative for reverse sort order
		ret.Sort((a, b) => -a.Score.CompareTo(b.Score));

		return ret;
	}

	public static List<RankResult<T>> RankParallel<T>(T src, T[] dsts, List<IClassifier<T>> classifiers, Func<T, T, bool> potentialEqualityCheck, MatchingEnv env, double maxMismatch) where T : Matchable {
		// return Arrays.stream(dsts)
		// 		.parallel()
		// 		.map(dst => rank(src, dst, classifiers, potentialEqualityCheck, env, maxMismatch))
		// 		.filter(Objects::nonNull)
		// 		.sorted(Comparator.<RankResult<T>, double>comparing(RankResult::getScore).reversed())
		// 		.collect(Collectors.toList());
		// TODO parallelization if we need it
		return Rank(src, dsts, classifiers, potentialEqualityCheck, env, maxMismatch);
	}

	private static RankResult<T>? Rank<T>(T src, T dst, List<IClassifier<T>> classifiers, Func<T, T, bool> potentialEqualityCheck, MatchingEnv env, double maxMismatch) where T : Matchable {
		// assert src.getEnv() != dst.getEnv();

		if (!potentialEqualityCheck.Invoke(src, dst)) return null;

		double score = 0;
		double mismatch = 0;
		List<ClassifierResult<T>> results = new(classifiers.Count);

		foreach (IClassifier<T> classifier in classifiers) {
			double cScore = classifier.GetScore(src, dst, env);
			// assert cScore > -epsilon && cScore < 1 + epsilon : "invalid score from "+classifier.getName()+": "+cScore;

			double weight = classifier.GetWeight();
			double weightedScore = cScore * weight;

			mismatch += weight - weightedScore;
			if (mismatch >= maxMismatch) return null;

			score += weightedScore;
			results.Add(new(classifier, cScore));
		}

		return new(dst, score, results);
	}

	public static bool CheckRank<T>(List<RankResult<T>> ranking, double absThreshold, double relThreshold, double maxScore) {
		if (ranking.Count == 0) return false;

		double score = GetScore(ranking[0].Score, maxScore);
		if (score < absThreshold) return false;

		if (ranking.Count == 1) {
			return true;
		} else {
			double nextScore = GetScore(ranking[1].Score, maxScore);

			return nextScore < score * (1 - relThreshold);
		}
	}

	public static double GetScore(double rawScore, double maxScore) {
		double ret = rawScore / maxScore;

		return ret * ret;
	}

	public static double GetRawScore(double score, double maxScore) {
		return Math.Sqrt(score) * maxScore;
	}

	public static void ExtractStrings(Collection<Instruction> il, ISet<string> out_) {
		foreach (var aInsn in il) {
			if (aInsn.OpCode == OpCodes.Ldstr) {
				out_.Add((string) aInsn.Operand);
			}
		}
	}

	public static void ExtractNumbers(MethodDefinition method, ISet<int> ints, ISet<long> longs, ISet<float> floats, ISet<double> doubles) {
		if(method.Body != null && method.Body.Instructions != null) {
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode == OpCodes.Ldc_I4_M1) {
					ints.Add(-1);
				} else if (instr.OpCode == OpCodes.Ldc_I4_0) {
					ints.Add(0);
				} else if (instr.OpCode == OpCodes.Ldc_I4_1) {
					ints.Add(1);
				} else if (instr.OpCode == OpCodes.Ldc_I4_2) {
					ints.Add(2);
				} else if (instr.OpCode == OpCodes.Ldc_I4_3) {
					ints.Add(3);
				} else if (instr.OpCode == OpCodes.Ldc_I4_4) {
					ints.Add(4);
				} else if (instr.OpCode == OpCodes.Ldc_I4_5) {
					ints.Add(5);
				} else if (instr.OpCode == OpCodes.Ldc_I4_6) {
					ints.Add(6);
				} else if (instr.OpCode == OpCodes.Ldc_I4_7) {
					ints.Add(7);
				} else if (instr.OpCode == OpCodes.Ldc_I4_8) {
					ints.Add(8);
				} else if (instr.OpCode == OpCodes.Ldc_I4_S ) {
					ints.Add((sbyte) instr.Operand);
				} else if (instr.OpCode == OpCodes.Ldc_I4) {
					ints.Add((int) instr.Operand);
				} else if (instr.OpCode == OpCodes.Ldc_I8) {
					longs.Add((long) instr.Operand);
				} else if (instr.OpCode == OpCodes.Ldc_R4) {
					floats.Add((float) instr.Operand);
				} else if (instr.OpCode == OpCodes.Ldc_R8) {
					doubles.Add((double) instr.Operand);
				}
			}
		}
	}

	public static void HandleNumberValue(object number, ISet<int> ints, ISet<long> longs, ISet<float> floats, ISet<double> doubles) {
		if (number == null) return;

		if (number is int i) {
			ints.Add(i);
		} else if (number is long l) {
			longs.Add(l);
		} else if (number is float f) {
			floats.Add(f);
		} else if (number is double d) {
			doubles.Add(d);
		}
	}

	public static double ClassifyPosition<T>(T a, T b,
			Func<T, int> positionSupplier,
			Func<T, int, T> siblingSupplier,
			Func<T, List<T>> siblingsSupplier) where T : Matchable {
		int posA = positionSupplier.Invoke(a);
		int posB = positionSupplier.Invoke(b);
		T[] siblingsA = [.. siblingsSupplier.Invoke(a)];
		T[] siblingsB = [.. siblingsSupplier.Invoke(b)];

		if (posA == posB && siblingsA.Length == siblingsB.Length) return 1;
		if (posA == -1 || posB == -1) return posA == posB ? 1 : 0;

		// try to find the index range enclosed by other mapped members and compare relative to it
		int startPosA = 0;
		int startPosB = 0;
		int endPosA = siblingsA.Length;
		int endPosB = siblingsB.Length;

		if (posA > 0) {
			for (int i = posA - 1; i >= 0; i--) {
				T c = siblingSupplier.Invoke(a, i);
				T? match = (T?) c.GetMatch();

				if (match != null) {
					startPosA = i + 1;
					startPosB = positionSupplier.Invoke(match) + 1;
					break;
				}
			}
		}

		if (posA < endPosA - 1) {
			for (int i = posA + 1; i < endPosA; i++) {
				T c = siblingSupplier.Invoke(a, i);
				T? match = (T?) c.GetMatch();

				if (match != null) {
					endPosA = i;
					endPosB = positionSupplier.Invoke(match);
					break;
				}
			}
		}

		if (startPosB >= endPosB || startPosB > posB || endPosB <= posB) {
			startPosA = startPosB = 0;
			endPosA = siblingsA.Length;
			endPosB = siblingsB.Length;
		}

		double relPosA = GetRelativePosition(posA - startPosA, endPosA - startPosA);
		// assert relPosA >= 0 && relPosA <= 1;
		double relPosB = GetRelativePosition(posB - startPosB, endPosB - startPosB);
		// assert relPosB >= 0 && relPosB <= 1;

		return 1 - Math.Abs(relPosA - relPosB);
	}

	public static double GetRelativePosition(int position, int size) {
		if (size == 1) return 0.5;
		// assert size > 1;

		return (double) position / (size - 1);
	}

	private const double epsilon = 1e-6;

	// private static readonly Logger logger = LoggerFactory.getLogger(ClassifierUtil.class); // TODO logging
	// private static readonly CacheToken<int[]> ilMapCacheToken = new CacheToken<>();
}
