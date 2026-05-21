using Mono.Cecil.Cil;

namespace Matcher.Matching.Classifier;

public class ClassifierUtil {
	// TODO make literally anything in here care about generics
	public static bool checkPotentialEquality(TypeInstance a, TypeInstance b) {
		if (a == b) return true;
		if (a.getMatch() != null) return a.getMatch() == b;
		if (b.getMatch() != null) return b.getMatch() == a;
		if (!a.isMatchable() || !b.isMatchable()) return false;
		// if (a.isArray() != b.isArray()) return false;
		// if (a.isArray() && !checkPotentialEquality(a.getElementClass(), b.getElementClass())) return false;
		if (!checkNameObfMatch(a, b)) return false;

		return true;
	}

	private static bool checkNameObfMatch(Matchable a, Matchable b) {
		bool nameObfA = a.isNameObfuscated;
		bool nameObfB = b.isNameObfuscated;

		if (nameObfA && nameObfB) { // both obf
			return true;
		} else if (nameObfA != nameObfB) { // one obf
			return Matcher.assumeBothOrNoneObfuscated;
		} else { // neither obf
			return a.getName().Equals(b.getName());
		}
	}

	public static bool checkPotentialEquality(MatchableMember a, MatchableMember b) {
		if (a is MethodInstance) {
			return checkPotentialEquality((MethodInstance) a, (MethodInstance) b);
		} else {
			return checkPotentialEquality((FieldInstance) a, (FieldInstance) b);
		}
	}

	public static bool checkPotentialEquality(MethodInstance a, MethodInstance b) {
		if (a == b) return true;
		if (a.getMatch() != null) return a.getMatch() == b;
		if (b.getMatch() != null) return b.getMatch() == a;
		if (!a.isMatchable() || !b.isMatchable()) return false;
		if (!checkPotentialEquality(a.containingType, b.containingType)) return false;
		if (!checkNameObfMatch(a, b)) return false;
		// if ((a.getId().StartsWith("<") || b.getId().StartsWith("<")) && !a.getName().Equals(b.getName())) return false; // require <clinit> and <init> to match

		//MethodInstance hierarchyMatch = a.getHierarchyMatch();
		//if (hierarchyMatch != null && !hierarchyMatch.getAllHierarchyMembers().contains(b)) return false;
		if ((a.hasHierarchyMatch() || b.hasHierarchyMatch()) && !a.hasMatchedHierarchy(b)) return false;

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

	public static bool checkPotentialEquality(FieldInstance a, FieldInstance b) {
		if (a == b) return true;
		if (a.getMatch() != null) return a.getMatch() == b;
		if (b.getMatch() != null) return b.getMatch() == a;
		if (!a.isMatchable() || !b.isMatchable()) return false;
		if (!checkPotentialEquality(a.containingType, b.containingType)) return false;
		if (!checkNameObfMatch(a, b)) return false;

		return true;
	}

	public static bool checkPotentialEquality(MethodParamInstance a, MethodParamInstance b) {
		if (a == b) return true;
		if (a.getMatch() != null) return a.getMatch() == b;
		if (b.getMatch() != null) return b.getMatch() == a;
		if (!a.isMatchable() || !b.isMatchable()) return false;
		// if (a.isArg() != b.isArg()) return false;
		if (!checkPotentialEquality(a.containingMethod, b.containingMethod)) return false;
		if (!checkNameObfMatch(a, b)) return false;

		return true;
	}

	public static bool checkPotentialEqualityNullable(TypeInstance a, TypeInstance b) {
		if (a == null && b == null) return true;
		if (a == null || b == null) return false;

		return checkPotentialEquality(a, b);
	}

	public static bool checkPotentialEqualityNullable(MethodInstance a, MethodInstance b) {
		if (a == null && b == null) return true;
		if (a == null || b == null) return false;

		return checkPotentialEquality(a, b);
	}

	public static bool checkPotentialEqualityNullable(FieldInstance a, FieldInstance b) {
		if (a == null && b == null) return true;
		if (a == null || b == null) return false;

		return checkPotentialEquality(a, b);
	}

	public static bool checkPotentialEqualityNullable(MethodParamInstance a, MethodParamInstance b) {
		if (a == null && b == null) return true;
		if (a == null || b == null) return false;

		return checkPotentialEquality(a, b);
	}

	public static double compareCounts(int countA, int countB) {
		int delta = Math.Abs(countA - countB);
		if (delta == 0) return 1;

		return 1 - (double) delta / Math.Max(countA, countB);
	}

	public static double compareSets<T>(ISet<T> setA, ISet<T> setB, bool readOnly) {
		if (readOnly) setB = new HashSet<T>(setB);

		int oldSize = setB.Count;
		setB.ExceptWith(setA);

		int matched = oldSize - setB.Count;
		int total = setA.Count - matched + oldSize;

		return total == 0 ? 1 : (double) matched / total;
	}

	public static double compareClassSets(List<TypeInstance> setA, List<TypeInstance> setB, bool readOnly) {
		return compareIdentitySets(new HashSet<TypeInstance>(setA, new IdentityEqualityComparer<TypeInstance>()), new HashSet<TypeInstance>(setB, new IdentityEqualityComparer<TypeInstance>()),
				readOnly, ClassifierUtil.checkPotentialEquality);
	}

	public static double compareClassSets(ISet<TypeInstance> setA, ISet<TypeInstance> setB, bool readOnly) {
		return compareIdentitySets(setA, setB, readOnly, ClassifierUtil.checkPotentialEquality);
	}

	public static double compareMethodSets(ISet<MethodInstance> setA, ISet<MethodInstance> setB, bool readOnly) {
		return compareIdentitySets(setA, setB, readOnly, ClassifierUtil.checkPotentialEquality);
	}

	public static double compareFieldSets(ISet<FieldInstance> setA, ISet<FieldInstance> setB, bool readOnly) {
		return compareIdentitySets(setA, setB, readOnly, ClassifierUtil.checkPotentialEquality);
	}

	private static double compareIdentitySets<T>(ISet<T> setA, ISet<T> setB, bool readOnly, Func<T, T, bool> comparator) where T : Matchable {
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
				} else if (a.getMatch() != null) {
					if (!setB.Remove((T?) a.getMatch())) {
						unmatched++;
					}

					toRemove.Add(a);
				} else if (assumeBothOrNoneObfuscated && !a.isNameObfuscated) {
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
				if (!b.isNameObfuscated) {
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

	public static double compareClassLists(List<TypeInstance> listA, List<TypeInstance> listB) {
		return compareLists(listA, listB, (list, ind) => list[ind], list => list.Count, (a, b) => ClassifierUtil.checkPotentialEquality(a, b) ? COMPARED_SIMILAR : COMPARED_DISTINCT);
	}

	public static double compareInsns(MethodInstance a, MethodInstance b) {
		var ilA = a.cecilMethod?.Body?.Instructions;
		var ilB = b.cecilMethod?.Body?.Instructions;
		if (ilA == null || ilB == null) return 1;

		return compareLists(ilA, ilB, (list, ind) => list[ind], list => list.Count, (inA, inB) => compareInsns(inA, inB, ilA, ilB, (list, item) => list.IndexOf(item), a, b, a.env.sharedEnv));
	}

	public static double compareInsns(List<Instruction> listA, List<Instruction> listB, MatchingEnv env) {
		return compareLists(listA, listB, (list, ind) => list[ind], list => list.Count, (inA, inB) => compareInsns(inA, inB, listA, listB, (list, item) => list.IndexOf(item), null, null, env));
	}

	private static int compareInsns<T>(Instruction insnA, Instruction insnB, T listA, T listB, Func<T, Instruction, int> posProvider,
			MethodInstance mthA, MethodInstance mthB, MatchingEnv env) {
		if (insnA.OpCode != insnB.OpCode) return COMPARED_DISTINCT;

		// TODO
		
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

		return COMPARED_SIMILAR;
	}

	// TODO these are used for comparing method invocations; will probably be different for us anyway
	// private static bool compareMethods(String ownerA, String nameA, String descA, bool toIfA, String ownerB, String nameB, String descB, bool toIfB, MatchingEnv env) {
	// 	TypeInstance clsA = env.envA.types[ownerA];
	// 	TypeInstance clsB = env.envB.types[ownerB];

	// 	if (clsA == null && clsB == null) return true;
	// 	if (clsA == null || clsB == null) return false;

	// 	return compareMethods(clsA, nameA, descA, toIfA, clsB, nameB, descB, toIfB);
	// }

	// private static bool compareMethods(TypeInstance ownerA, String nameA, String descA, bool toIfA, TypeInstance ownerB, String nameB, String descB, bool toIfB) {
	// 	MethodInstance methodA = ownerA.resolveMethod(nameA, descA, toIfA);
	// 	MethodInstance methodB = ownerB.resolveMethod(nameB, descB, toIfB);

	// 	if (methodA == null && methodB == null) return true;
	// 	if (methodA == null || methodB == null) return false;

	// 	return checkPotentialEquality(methodA, methodB);
	// }

	private static double compareLists<T, U>(T listA, T listB, RetrieveListElement<T, U> elementRetriever, RetrieveListSize<T> sizeRetriever, CompareElements<U> elementComparator) {
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

	public static int[]? mapInsns(MethodInstance a, MethodInstance b) {
		var ilA = a.cecilMethod?.Body?.Instructions;
		var ilB = b.cecilMethod?.Body?.Instructions;
		if (ilA == null || ilB == null) return null;

		// if (ilA.Count * ilB.Count < 1000) {
			return mapInsns(ilA, ilB, a, b, a.env.sharedEnv);
		// } else {
		// 	return a.env.sharedEnv.getCache().compute(ilMapCacheToken, a, b, (mA, mB) => mapInsns(mA.getAsmNode().instructions, mB.getAsmNode().instructions, mA, mB, mA.env.sharedEnv));
		// }
	}

	public static int[] mapInsns(Collection<Instruction> listA, Collection<Instruction> listB, MethodInstance mthA, MethodInstance mthB, MatchingEnv env) {
		return mapLists(listA, listB, (list, ind) => list[ind], list => list.Count, (inA, inB) => compareInsns(inA, inB, listA, listB, (list, item) => list.IndexOf(item), mthA, mthB, env));
	}

	private static int[] mapLists<T, U>(T listA, T listB, RetrieveListElement<T, U> elementRetriever, RetrieveListSize<T> sizeRetriever, CompareElements<U> elementComparator) {
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

	private static string? toString(Object node) {
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

	public static List<RankResult<T>> rank<T>(T src, T[] dsts, List<IClassifier<T>> classifiers, Func<T, T, bool> potentialEqualityCheck, MatchingEnv env, double maxMismatch) where T : Matchable {
		List<RankResult<T>> ret = new(dsts.Length);

		foreach (T dst in dsts) {
			RankResult<T>? result = rank(src, dst, classifiers, potentialEqualityCheck, env, maxMismatch);
			if (result != null) ret.Add(result);
		}

		// negative for reverse sort order
		// TODO verify this sorting is correct
		ret.Sort((a, b) => -a.score.CompareTo(b.score));

		return ret;
	}

	public static List<RankResult<T>> rankParallel<T>(T src, T[] dsts, List<IClassifier<T>> classifiers, Func<T, T, bool> potentialEqualityCheck, MatchingEnv env, double maxMismatch) where T : Matchable {
		// return Arrays.stream(dsts)
		// 		.parallel()
		// 		.map(dst => rank(src, dst, classifiers, potentialEqualityCheck, env, maxMismatch))
		// 		.filter(Objects::nonNull)
		// 		.sorted(Comparator.<RankResult<T>, double>comparing(RankResult::getScore).reversed())
		// 		.collect(Collectors.toList());
		// TODO parallelization if we need it
		return rank(src, dsts, classifiers, potentialEqualityCheck, env, maxMismatch);
	}

	private static RankResult<T>? rank<T>(T src, T dst, List<IClassifier<T>> classifiers, Func<T, T, bool> potentialEqualityCheck, MatchingEnv env, double maxMismatch) where T : Matchable {
		// assert src.getEnv() != dst.getEnv();

		if (!potentialEqualityCheck.Invoke(src, dst)) return null;

		double score = 0;
		double mismatch = 0;
		List<ClassifierResult<T>> results = new(classifiers.Count);

		foreach (IClassifier<T> classifier in classifiers) {
			double cScore = classifier.getScore(src, dst, env);
			// assert cScore > -epsilon && cScore < 1 + epsilon : "invalid score from "+classifier.getName()+": "+cScore;

			double weight = classifier.getWeight();
			double weightedScore = cScore * weight;

			mismatch += weight - weightedScore;
			if (mismatch >= maxMismatch) return null;

			score += weightedScore;
			results.Add(new(classifier, cScore));
		}

		return new(dst, score, results);
	}

	public static bool checkRank<T>(List<RankResult<T>> ranking, double absThreshold, double relThreshold, double maxScore) {
		if (ranking.Count == 0) return false;

		double score = getScore(ranking[0].score, maxScore);
		if (score < absThreshold) return false;

		if (ranking.Count == 1) {
			return true;
		} else {
			double nextScore = getScore(ranking[1].score, maxScore);

			return nextScore < score * (1 - relThreshold);
		}
	}

	public static double getScore(double rawScore, double maxScore) {
		double ret = rawScore / maxScore;

		return ret * ret;
	}

	public static double getRawScore(double score, double maxScore) {
		return Math.Sqrt(score) * maxScore;
	}

	public static void extractStrings(Collection<Instruction> il, ISet<string> out_) {
		foreach (var aInsn in il) {
			if (aInsn.OpCode == OpCodes.Ldstr) {
				out_.Add((string) aInsn.Operand);
			}
		}
	}

	// public static void extractNumbers(MethodNode node, ISet<int> ints, ISet<long> longs, ISet<float> floats, ISet<double> doubles) {
	// 	foreach (var aInsn in node.instructions) {
	// 		if (aInsn is LdcInsnNode) {
	// 			LdcInsnNode insn = (LdcInsnNode) aInsn;

	// 			handleNumberValue(insn.cst, ints, longs, floats, doubles);
	// 		} else if (aInsn is IntInsnNode) {
	// 			IntInsnNode insn = (IntInsnNode) aInsn;

	// 			ints.Add(insn.operand);
	// 		}
	// 	}
	// }

	public static void handleNumberValue(object number, ISet<int> ints, ISet<long> longs, ISet<float> floats, ISet<double> doubles) {
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

	public static double classifyPosition<T>(T a, T b,
			Func<T, int> positionSupplier,
			Func<T, int, T> siblingSupplier,
			Func<T, T[]> siblingsSupplier) where T : Matchable {
		int posA = positionSupplier.Invoke(a);
		int posB = positionSupplier.Invoke(b);
		T[] siblingsA = siblingsSupplier.Invoke(a);
		T[] siblingsB = siblingsSupplier.Invoke(b);

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
				T? match = (T?) c.getMatch();

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
				T? match = (T?) c.getMatch();

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

		double relPosA = getRelativePosition(posA - startPosA, endPosA - startPosA);
		// assert relPosA >= 0 && relPosA <= 1;
		double relPosB = getRelativePosition(posB - startPosB, endPosB - startPosB);
		// assert relPosB >= 0 && relPosB <= 1;

		return 1 - Math.Abs(relPosA - relPosB);
	}

	private static double getRelativePosition(int position, int size) {
		if (size == 1) return 0.5;
		// assert size > 1;

		return (double) position / (size - 1);
	}

	private const double epsilon = 1e-6;

	// private static readonly Logger logger = LoggerFactory.getLogger(ClassifierUtil.class); // TODO logging
	// private static readonly CacheToken<int[]> ilMapCacheToken = new CacheToken<>();
}
