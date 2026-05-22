namespace Matcher.Matching.Classifier;

public class MethodParamClassifier {
	public static void Init() {
		AddClassifier(type, 10);
		// addClassifier(position, 3);
		// addClassifier(lvIndex, 2);
		// addClassifier(usage, 8);
	}

	public static void AddClassifier(AbstractClassifier classifier, double weight, params ClassifierLevel[] levels) {
		if (levels.Length == 0) levels = Enum.GetValues<ClassifierLevel>();

		classifier.weight = weight;

		foreach (ClassifierLevel level in levels) {
			if (!classifiers.ContainsKey(level)) classifiers[level] = [];
			classifiers[level].Add(classifier);
			maxScore[level] = GetMaxScore(level) + weight;
		}
	}

	public static double GetMaxScore(ClassifierLevel level) {
		return maxScore.GetValueOrDefault(level, 0);
	}

	public static List<RankResult<MethodParamInstance>> Rank(MethodParamInstance src, MethodParamInstance[] dsts, ClassifierLevel level, MatchingEnv env, double maxMismatch) {
		return ClassifierUtil.Rank(src, dsts, classifiers.GetValueOrDefault(level, []), ClassifierUtil.CheckPotentialEquality, env, maxMismatch);
	}

	private static readonly Dictionary<ClassifierLevel, List<IClassifier<MethodParamInstance>>> classifiers = [];
	private static readonly Dictionary<ClassifierLevel, double> maxScore = [];

	private static readonly AbstractClassifier type = new("type", (argA, argB, env) => {
			return ClassifierUtil.CheckPotentialEquality(argA.paramType, argB.paramType) ? 1 : 0;
		}
	);

	// private static readonly AbstractClassifier position = new("position", (argA, argB, env) => {
	// 		return ClassifierUtil.classifyPosition(methodA, methodB,
	// 				MethodParamInstance.getIndex,
	// 				(a, idx) => (a.isArg() ? a.getMethod().getArg(idx) : a.getMethod().getVar(idx)),
	// 				a => (a.isArg() ? a.getMethod().getArgs() : a.getMethod().getVars()));
	// 	}
	// );

	// private static readonly AbstractClassifier lvIndex = new("lv index", (argA, argB, env) => {
	// 		return argA.getLvIndex() == argB.getLvIndex() ? 1 : 0;
	// 	}
	// );

	// private static readonly AbstractClassifier usage = new("usage", (argA, argB, env) => {
	// 		int[] map = ClassifierUtil.mapInsns(argA.getMethod(), argB.getMethod());
	// 		if (map == null) return 1;

	// 		InsnList ilA = argA.getMethod().getAsmNode().instructions;
	// 		InsnList ilB = argB.getMethod().getAsmNode().instructions;
	// 		int matched = 0;
	// 		int mismatched = 0;

	// 		for (int srcIdx = 0; srcIdx < map.Length; srcIdx++) {
	// 			int dstIdx = map[srcIdx];
	// 			if (dstIdx < 0) continue;

	// 			AbstractInsnNode inA = ilA.get(srcIdx);
	// 			AbstractInsnNode inB = ilB.get(dstIdx);
	// 			int varA, varB;

	// 			if (inA.getType() == AbstractInsnNode.VAR_INSN) {
	// 				varA = ((VarInsnNode) inA).var;
	// 				varB = ((VarInsnNode) inB).var;
	// 			} else if (inA.getType() == AbstractInsnNode.IINC_INSN) {
	// 				varA = ((IincInsnNode) inA).var;
	// 				varB = ((IincInsnNode) inB).var;
	// 			} else {
	// 				continue;
	// 			}

	// 			if (varA == argA.getLvIndex() && (argA.getStartInsn() < 0 || srcIdx >= argA.getStartInsn() && srcIdx < argA.getEndInsn())) {
	// 				if (varB == argB.getLvIndex() && (argB.getStartInsn() < 0 || dstIdx >= argB.getStartInsn() && dstIdx < argB.getEndInsn())) {
	// 					matched++;
	// 				} else {
	// 					mismatched++;
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

	public class AbstractClassifier(string name, Func<MethodParamInstance, MethodParamInstance, MatchingEnv, double> classifierFunc) : IClassifier<MethodParamInstance> {
		private readonly string name = name;
		public double weight; // probably shouldn't be public but I'm lazy and csharp nested types have different visibility rules so I can't just do private
		private readonly Func<MethodParamInstance, MethodParamInstance, MatchingEnv, double> classifierFunc = classifierFunc;

		public string GetName() {
			return name;
		}

		public double GetWeight() {
			return weight;
		}

		public double GetScore(MethodParamInstance a, MethodParamInstance b, MatchingEnv env) {
			return classifierFunc.Invoke(a, b, env);
		}
	}
}
