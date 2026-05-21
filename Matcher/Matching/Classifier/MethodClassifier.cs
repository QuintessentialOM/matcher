using System.Linq;
using Matcher.Matching;

namespace Matcher.Matching.Classifier;

public class MethodClassifier {
	public static void init() {
		addClassifier(methodTypeCheck, 10);
		addClassifier(accessFlags, 4);
		// addClassifier(argTypes, 10);
		// addClassifier(retType, 5);
		// addClassifier(signature, 5);
		// addClassifier(classRefs, 3);
		// addClassifier(stringConstants, 5);
		// addClassifier(numericConstants, 5);
		// addClassifier(parentMethods, 10);
		// addClassifier(childMethods, 3);
		// addClassifier(inReferences, 6);
		// addClassifier(outReferences, 6);
		// addClassifier(fieldReads, 5);
		// addClassifier(fieldWrites, 5);
		// addClassifier(position, 3);
		// addClassifier(code, 12, ClassifierLevel.Full, ClassifierLevel.Extra);
		// addClassifier(inRefsBci, 6, ClassifierLevel.Extra);
	}

	public static void addClassifier(AbstractClassifier classifier, double weight, params ClassifierLevel[] levels) {
		if (levels.Length == 0) levels = Enum.GetValues<ClassifierLevel>();

		classifier.weight = weight;

		foreach (ClassifierLevel level in levels) {
			if (!classifiers.ContainsKey(level)) classifiers[level] = new();
			classifiers[level].Add(classifier);
			maxScore[level] = getMaxScore(level) + weight;
		}
	}

	public static double getMaxScore(ClassifierLevel level) {
		return maxScore.GetValueOrDefault(level, 0);
	}

	public static List<RankResult<MethodInstance>> rank(MethodInstance src, MethodInstance[] dsts, ClassifierLevel level, MatchingEnv env) {
		return rank(src, dsts, level, env, double.PositiveInfinity);
	}

	public static List<RankResult<MethodInstance>> rank(MethodInstance src, MethodInstance[] dsts, ClassifierLevel level, MatchingEnv env, double maxMismatch) {
		if (src.hasMatch()) { // already matched,  limit dsts to the match
			if (dsts.Contains(src.getMatch())) {
				return new();
			} else if (dsts.Length != 1) {
				dsts = [ src.getMatch()! ];
			}
		} else { // limit dsts to the same method tree if there's a matched src
			// MethodInstance matched = src.getHierarchyMatch();
			// possibly not the same semantics as getHierarchyMatch, unsure, matcher source is kind of confusing
			var matched = src.hierarchyData?.matchedHierarchy;

			if (matched != null) {
				ISet<MethodInstance> dstHierarchyMembers = matched.members;
				MethodInstance[] newDsts = new MethodInstance[dsts.Length];
				int writeIdx = 0;

				for (int readIdx = 0; readIdx < dsts.Length; readIdx++) {
					MethodInstance m = dsts[readIdx];

					if (dstHierarchyMembers.Contains(m)) {
						newDsts[writeIdx++] = m;
					}
				}

				if (writeIdx == 0) return [];
				if (writeIdx < newDsts.Length) newDsts = Utils.CopyArray(newDsts, writeIdx);

				dsts = newDsts;
			}
		}

		return ClassifierUtil.rank(src, dsts, classifiers.GetValueOrDefault(level, []), ClassifierUtil.checkPotentialEquality, env, maxMismatch);
	}

	private static readonly Dictionary<ClassifierLevel, List<IClassifier<MethodInstance>>> classifiers = new();
	private static readonly Dictionary<ClassifierLevel, double> maxScore = new();

	private static AbstractClassifier methodTypeCheck = new AbstractClassifier("method type check", (MethodInstance methodA, MethodInstance methodB, MatchingEnv env) => {
			if (!checkAsmNodes(methodA, methodB)) return compareAsmNodes(methodA, methodB);

			int diff = 0;

			diff += methodA.cecilMethod.IsStatic != methodB.cecilMethod.IsStatic ? 1 : 0;
			diff += methodA.cecilMethod.IsNative != methodB.cecilMethod.IsNative ? 1 : 0;
			diff += methodA.cecilMethod.IsAbstract != methodB.cecilMethod.IsAbstract ? 1 : 0;

			return 1 - diff / 3.0;
		}
	);

	private static AbstractClassifier accessFlags = new AbstractClassifier("access flags", (MethodInstance methodA, MethodInstance methodB, MatchingEnv env) => {
			if (!checkAsmNodes(methodA, methodB)) return compareAsmNodes(methodA, methodB);

			// int mask = (Opcodes.ACC_PUBLIC | Opcodes.ACC_PROTECTED | Opcodes.ACC_PRIVATE) | Opcodes.ACC_FINAL | Opcodes.ACC_SYNCHRONIZED | Opcodes.ACC_BRIDGE | Opcodes.ACC_VARARGS | Opcodes.ACC_STRICT | Opcodes.ACC_SYNTHETIC;
			// int resultA = methodA.getAsmNode().access & mask;
			// int resultB = methodB.getAsmNode().access & mask;

			// return 1 - int.bitCount(resultA ^ resultB) / 8.0;

			int diff = 0;

			bool hasSameAccess = (methodA.cecilMethod.IsPublic == methodB.cecilMethod.IsPublic)
				&& (methodA.cecilMethod.IsFamilyOrAssembly == methodB.cecilMethod.IsFamilyOrAssembly)
				&& (methodA.cecilMethod.IsFamily == methodB.cecilMethod.IsFamily)
				&& (methodA.cecilMethod.IsFamilyAndAssembly == methodB.cecilMethod.IsFamilyAndAssembly)
				&& (methodA.cecilMethod.IsAssembly == methodB.cecilMethod.IsAssembly)
				&& (methodA.cecilMethod.IsPrivate == methodB.cecilMethod.IsPrivate);

			if (!hasSameAccess) diff += 1;

			// TODO method flags other than access

			return 1 - diff;
		}
	);

	// private static AbstractClassifier argTypes = new AbstractClassifier("arg types", (MethodInstance methodA, MethodInstance methodB, MatchingEnv env) => {
	// 		return ClassifierUtil.compareClassLists(getArgTypes(methodA), getArgTypes(methodB));
	// 	}
	// );

	// private static List<TypeInstance> getArgTypes(MethodInstance method) {
	// 	MethodVarInstance[] args = method.getArgs();
	// 	if (argsdsts.Length == 0) return Collections.emptyList();

	// 	List<TypeInstance> ret = new ArrayList<>(argsdsts.Length);

	// 	for (MethodVarInstance arg : args) {
	// 		ret.add(arg.getType());
	// 	}

	// 	return ret;
	// }

	// private static AbstractClassifier retType = new AbstractClassifier("ret type", (MethodInstance methodA, MethodInstance methodB, MatchingEnv env) => {
	// 		return ClassifierUtil.checkPotentialEquality(methodA.getRetType(), methodB.getRetType()) ? 1 : 0;
	// 	}
	// );

	// private static AbstractClassifier signature = new AbstractClassifier("signature", (MethodInstance methodA, MethodInstance methodB, MatchingEnv env) => {
	// 		MethodSignature sigA = methodA.getSignature();
	// 		MethodSignature sigB = methodB.getSignature();

	// 		if (sigA == null && sigB == null) return 1;
	// 		if (sigA == null || sigB == null) return 0;

	// 		return sigA.isPotentiallyEqual(sigB) ? 1 : 0;
	// 	}
	// );

	// private static AbstractClassifier classRefs = new AbstractClassifier("class refs", (MethodInstance methodA, MethodInstance methodB, MatchingEnv env) => {
	// 		return ClassifierUtil.compareClassSets(methodA.getClassRefs(), methodB.getClassRefs(), true);
	// 	}
	// );

	// private static AbstractClassifier stringConstants = new AbstractClassifier("string constants", (MethodInstance methodA, MethodInstance methodB, MatchingEnv env) => {
	// 		if (!checkAsmNodes(methodA, methodB)) return compareAsmNodes(methodA, methodB);

	// 		HashSet<String> stringsA = new();
	// 		ClassifierUtil.extractStrings(methodA.getAsmNode().instructions, stringsA);
	// 		HashSet<String> stringsB = new();
	// 		ClassifierUtil.extractStrings(methodB.getAsmNode().instructions, stringsB);

	// 		return ClassifierUtil.compareSets(stringsA, stringsB, false);
	// 	}
	// );

	// private static AbstractClassifier numericConstants = new AbstractClassifier("numeric constants", (MethodInstance methodA, MethodInstance methodB, MatchingEnv env) => {
	// 		if (!checkAsmNodes(methodA, methodB)) return compareAsmNodes(methodA, methodB);

	// 		HashSet<int> intsA = new();
	// 		HashSet<int> intsB = new();
	// 		HashSet<long> longsA = new();
	// 		HashSet<long> longsB = new();
	// 		HashSet<float> floatsA = new();
	// 		HashSet<float> floatsB = new();
	// 		HashSet<double> doublesA = new();
	// 		HashSet<double> doublesB = new();

	// 		ClassifierUtil.extractNumbers(methodA.getAsmNode(), intsA, longsA, floatsA, doublesA);
	// 		ClassifierUtil.extractNumbers(methodB.getAsmNode(), intsB, longsB, floatsB, doublesB);

	// 		return (ClassifierUtil.compareSets(intsA, intsB, false)
	// 				+ ClassifierUtil.compareSets(longsA, longsB, false)
	// 				+ ClassifierUtil.compareSets(floatsA, floatsB, false)
	// 				+ ClassifierUtil.compareSets(doublesA, doublesB, false)) / 4.0;
	// 	}
	// );

	// private static AbstractClassifier parentMethods = new AbstractClassifier("parent methods", (MethodInstance methodA, MethodInstance methodB, MatchingEnv env) => {
	// 		return ClassifierUtil.compareMethodSets(methodA.getParents(), methodB.getParents(), true);
	// 	}
	// );

	// private static AbstractClassifier childMethods = new AbstractClassifier("child methods", (MethodInstance methodA, MethodInstance methodB, MatchingEnv env) => {
	// 		return ClassifierUtil.compareMethodSets(methodA.getChildren(), methodB.getChildren(), true);
	// 	}
	// );

	// private static AbstractClassifier outReferences = new AbstractClassifier("out references", (MethodInstance methodA, MethodInstance methodB, MatchingEnv env) => {
	// 		return ClassifierUtil.compareMethodSets(methodA.getRefsOut(), methodB.getRefsOut(), true);
	// 	}
	// );

	// private static AbstractClassifier inReferences = new AbstractClassifier("in references", (MethodInstance methodA, MethodInstance methodB, MatchingEnv env) => {
	// 		return ClassifierUtil.compareMethodSets(methodA.getRefsIn(), methodB.getRefsIn(), true);
	// 	}
	// );

	// private static AbstractClassifier fieldReads = new AbstractClassifier("field reads", (MethodInstance methodA, MethodInstance methodB, MatchingEnv env) => {
	// 		return ClassifierUtil.compareFieldSets(methodA.getFieldReadRefs(), methodB.getFieldReadRefs(), true);
	// 	}
	// );

	// private static AbstractClassifier fieldWrites = new AbstractClassifier("field writes", (MethodInstance methodA, MethodInstance methodB, MatchingEnv env) => {
	// 		return ClassifierUtil.compareFieldSets(methodA.getFieldWriteRefs(), methodB.getFieldWriteRefs(), true);
	// 	}
	// );

	// private static AbstractClassifier position = new AbstractClassifier("position", (MethodInstance methodA, MethodInstance methodB, MatchingEnv env) => {
	// 		return ClassifierUtil.classifyPosition(methodA, methodB, MemberInstance::getPosition, (m, idx) -> m.getCls().getMethod(idx), m -> m.getCls().getMethods());
	// 	}
	// );

	// private static AbstractClassifier code = new AbstractClassifier("code", (MethodInstance methodA, MethodInstance methodB, MatchingEnv env) => {
	// 		if (!checkAsmNodes(methodA, methodB)) return compareAsmNodes(methodA, methodB);

	// 		return ClassifierUtil.compareInsns(methodA, methodB);
	// 	}
	// );

	// private static AbstractClassifier inRefsBci = new AbstractClassifier("in refs (bci)", (MethodInstance methodA, MethodInstance methodB, MatchingEnv env) => {
	// 		String ownerA = methodA.getCls().getName();
	// 		String nameA = methodA.getName();
	// 		String descA = methodA.getDesc();
	// 		String ownerB = methodB.getCls().getName();
	// 		String nameB = methodB.getName();
	// 		String descB = methodB.getDesc();

	// 		int matched = 0;
	// 		int mismatched = 0;

	// 		foreach (MethodInstance src in methodA.getRefsIn()) {
	// 			if (src == methodA) continue;

	// 			MethodInstance dst = src.getMatch();

	// 			if (dst == null || !methodB.getRefsIn().contains(dst)) {
	// 				mismatched++;
	// 				continue;
	// 			}

	// 			int[]? map = ClassifierUtil.mapInsns(src, dst!);
	// 			if (map == null) continue;

	// 			InsnList ilA = src.getAsmNode().instructions;
	// 			InsnList ilB = dst!.getAsmNode().instructions;

	// 			for (int srcIdx = 0; srcIdx < mapdsts.Length; srcIdx++) {
	// 				if (map[srcIdx] < 0) continue;

	// 				AbstractInsnNode in = ilA.get(srcIdx);
	// 				int type = in.getType();
	// 				if (type != AbstractInsnNode.METHOD_INSN && type != AbstractInsnNode.INVOKE_DYNAMIC_INSN) continue;

	// 				if (!isSameMethod(in, ownerA, nameA, descA, methodA)) continue;

	// 				in = ilB.get(map[srcIdx]);

	// 				if (!isSameMethod(in, ownerB, nameB, descB, methodB)) {
	// 					mismatched++;
	// 				} else {
	// 					matched++;
	// 				}
	// 			}
	// 		}

	// 		if (matched == 0 && mismatched == 0) {
	// 			return 1;
	// 		} else {
	// 			return (double) matched / (matched + mismatched);
	// 		}
	// 	}
	// );

	// private static bool isSameMethod(AbstractInsnNode in_, String owner, String name, String desc, MethodInstance method) {
	// 	String sOwner, sName, sDesc;
	// 	bool sItf;

	// 	if (in_.getType() == AbstractInsnNode.METHOD_INSN) {
	// 		MethodInsnNode min = (MethodInsnNode) in_;
	// 		sOwner = min.owner;
	// 		sName = min.name;
	// 		sDesc = min.desc;
	// 		sItf = min.itf;
	// 	} else {
	// 		InvokeDynamicInsnNode din = (InvokeDynamicInsnNode) in_;
	// 		Handle impl = Util.getTargetHandle(din.bsm, din.bsmArgs);
	// 		if (impl == null) return false;

	// 		int tag = impl.getTag();
	// 		if (tag < Opcodes.H_INVOKEVIRTUAL || tag > Opcodes.H_INVOKEINTERFACE) return false;

	// 		sOwner = impl.getOwner();
	// 		sName = impl.getName();
	// 		sDesc = impl.getDesc();
	// 		sItf = Util.isCallToInterface(impl);
	// 	}

	// 	TypeInstance target;

	// 	return sName.equals(name)
	// 			&& sDesc.equals(desc)
	// 			&& (sOwner.equals(owner) || (target = method.getEnv().getClsByName(sOwner)) != null && target.resolveMethod(name, desc, sItf) == method);
	// }

	private static bool checkAsmNodes(MethodInstance a, MethodInstance b) {
		return a.cecilMethod != null && b.cecilMethod != null;
	}

	private static double compareAsmNodes(MethodInstance a, MethodInstance b) {
		return a.cecilMethod == null && b.cecilMethod == null ? 1 : 0;
	}

	public class AbstractClassifier : IClassifier<MethodInstance> {
		private readonly string name;
		public double weight; // probably shouldn't be public but I'm lazy and csharp nested types have different visibility rules so I can't just do private
		private Func<MethodInstance, MethodInstance, MatchingEnv, double> classifierFunc;

		public AbstractClassifier(string name, Func<MethodInstance, MethodInstance, MatchingEnv, double> classifierFunc) {
			this.name = name;
			this.classifierFunc = classifierFunc;
		}

		public String getName() {
			return name;
		}

		public double getWeight() {
			return weight;
		}

		public double getScore(MethodInstance a, MethodInstance b, MatchingEnv env) {
			return classifierFunc.Invoke(a, b, env);
		}
	}
}