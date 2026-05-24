using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Matcher.Matching.Classifier;

public class MethodParamClassifier {
	public static void Init() {
		AddClassifier(type, 10);
		AddClassifier(position, 3);
		// AddClassifier(lvIndex, 2);
		AddClassifier(usage, 8);
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

	private static readonly AbstractClassifier position = new("position", (argA, argB, env) => {
			return ClassifierUtil.ClassifyPosition(argA, argB,
					param => param.position,
					(a, idx) => a.ContainingMethod.args[idx],// (a, idx) => (a.isArg() ? a.getMethod().getArg(idx) : a.getMethod().getVar(idx)),
					a => [.. a.ContainingMethod.args]);// a => (a.isArg() ? a.getMethod().getArgs() : a.getMethod().getVars()));
		}
	);

	// private static readonly AbstractClassifier lvIndex = new("lv index", (argA, argB, env) => {
	// 		return argA.getLvIndex() == argB.getLvIndex() ? 1 : 0;
	// 	}
	// );

	private static readonly AbstractClassifier usage = new("usage", (argA, argB, env) => {
			int[]? map = ClassifierUtil.MapInsns(argA.ContainingMethod, argB.ContainingMethod);
			if (map == null) return 1;

			var ilA = argA.ContainingMethod.CecilMethod!.Body.Instructions;
			var ilB = argB.ContainingMethod.CecilMethod!.Body.Instructions;

			int matched = 0;
			int mismatched = 0;

			for (int srcIdx = 0; srcIdx < map.Length; srcIdx++) {
				int dstIdx = map[srcIdx];
				if (dstIdx < 0) continue;

				var inA = ilA[srcIdx];
				var inB = ilB[dstIdx];
				int varA, varB;

				if (inA.Operand is ParameterDefinition paramA) varA = paramA.Index;
				else if (inA.OpCode == OpCodes.Ldarg_0) varA = 0;
				else if (inA.OpCode == OpCodes.Ldarg_1) varA = 1;
				else if (inA.OpCode == OpCodes.Ldarg_2) varA = 2;
				else if (inA.OpCode == OpCodes.Ldarg_3) varA = 3;
				else continue;

				if (inB.Operand is ParameterDefinition paramB) varB = paramB.Index;
				else if (inB.OpCode == OpCodes.Ldarg_0) varB = 0;
				else if (inB.OpCode == OpCodes.Ldarg_1) varB = 1;
				else if (inB.OpCode == OpCodes.Ldarg_2) varB = 2;
				else if (inB.OpCode == OpCodes.Ldarg_3) varB = 3;
				else continue;

				if (varA == argA.position) {
					if (varB == argB.position) {
						matched++;
					} else {
						mismatched++;
					}
				}

				// more complex logic for handling locals as well
				// if (varA == argA.getLvIndex() && (argA.getStartInsn() < 0 || srcIdx >= argA.getStartInsn() && srcIdx < argA.getEndInsn())) {
				// 	if (varB == argB.getLvIndex() && (argB.getStartInsn() < 0 || dstIdx >= argB.getStartInsn() && dstIdx < argB.getEndInsn())) {
				// 		matched++;
				// 	} else {
				// 		mismatched++;
				// 	}
				// }
			}

			if (matched == 0 && mismatched == 0) {
				return 1;
			} else {
				return (double) matched / (matched + mismatched);
			}
		}
	);

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
