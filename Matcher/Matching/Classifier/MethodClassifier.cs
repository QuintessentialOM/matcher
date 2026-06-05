using System.Linq;
using Mono.Cecil;

namespace Matcher.Matching.Classifier;

public class MethodClassifier {
	public static void Init() {
		AddClassifier(methodTypeCheck, 10);
		AddClassifier(accessFlags, 4);
		AddClassifier(argTypes, 10);
		AddClassifier(retType, 5);
		// addClassifier(signature, 5);
		AddClassifier(classRefs, 3);
		AddClassifier(stringConstants, 5);
		AddClassifier(numericConstants, 5);
		AddClassifier(parentMethods, 10);
		AddClassifier(childMethods, 3);
		AddClassifier(inReferences, 6);
		AddClassifier(outReferences, 6);
		AddClassifier(fieldReads, 5);
		AddClassifier(fieldWrites, 5);
		AddClassifier(position, 3);
		AddClassifier(code, 12, ClassifierLevel.Full, ClassifierLevel.Extra);
		AddClassifier(inRefsBci, 6, ClassifierLevel.Extra);
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

	public static List<RankResult<MethodInstance>> Rank(MethodInstance src, MethodInstance[] dsts, ClassifierLevel level, MatchingEnv env) {
		return Rank(src, dsts, level, env, double.PositiveInfinity);
	}

	public static List<RankResult<MethodInstance>> Rank(MethodInstance src, MethodInstance[] dsts, ClassifierLevel level, MatchingEnv env, double maxMismatch) {
		if (src.HasMatch()) { // already matched,  limit dsts to the match
			if (dsts.Contains(src.GetMatch())) {
				return [];
			} else if (dsts.Length != 1) {
				dsts = [ src.GetMatch()! ];
			}
		} else { // limit dsts to the same method tree if there's a matched src
			// MethodInstance matched = src.getHierarchyMatch();
			// possibly not the same semantics as getHierarchyMatch, unsure, matcher source is kind of confusing
			var matched = src.hierarchyData?.MatchedHierarchy;

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

		return ClassifierUtil.Rank(src, dsts, classifiers.GetValueOrDefault(level, []), ClassifierUtil.CheckPotentialEquality, env, maxMismatch);
	}

	private static readonly Dictionary<ClassifierLevel, List<IClassifier<MethodInstance>>> classifiers = [];
	private static readonly Dictionary<ClassifierLevel, double> maxScore = [];

	private static readonly AbstractClassifier methodTypeCheck = new("method type check", (methodA, methodB, env) => {
			if (!CheckAsmNodes(methodA, methodB)) return CompareAsmNodes(methodA, methodB);

			int diff = 0;

			diff += methodA.CecilMethod.IsStatic != methodB.CecilMethod.IsStatic ? 1 : 0;
			diff += methodA.CecilMethod.IsNative != methodB.CecilMethod.IsNative ? 1 : 0;
			diff += methodA.CecilMethod.IsAbstract != methodB.CecilMethod.IsAbstract ? 1 : 0;

			return 1 - diff / 3.0;
		}
	);

	private static readonly AbstractClassifier accessFlags = new("access flags", (methodA, methodB, env) => {
			if (!CheckAsmNodes(methodA, methodB)) return CompareAsmNodes(methodA, methodB);

			// int mask = (Opcodes.ACC_PUBLIC | Opcodes.ACC_PROTECTED | Opcodes.ACC_PRIVATE) | Opcodes.ACC_FINAL | Opcodes.ACC_SYNCHRONIZED | Opcodes.ACC_BRIDGE | Opcodes.ACC_VARARGS | Opcodes.ACC_STRICT | Opcodes.ACC_SYNTHETIC;
			// int resultA = methodA.getAsmNode().access & mask;
			// int resultB = methodB.getAsmNode().access & mask;

			// return 1 - int.bitCount(resultA ^ resultB) / 8.0;

			int diff = 0;

			bool hasSameAccess = (methodA.CecilMethod.IsPublic == methodB.CecilMethod.IsPublic)
				&& (methodA.CecilMethod.IsFamilyOrAssembly == methodB.CecilMethod.IsFamilyOrAssembly)
				&& (methodA.CecilMethod.IsFamily == methodB.CecilMethod.IsFamily)
				&& (methodA.CecilMethod.IsFamilyAndAssembly == methodB.CecilMethod.IsFamilyAndAssembly)
				&& (methodA.CecilMethod.IsAssembly == methodB.CecilMethod.IsAssembly)
				&& (methodA.CecilMethod.IsPrivate == methodB.CecilMethod.IsPrivate);

			if (!hasSameAccess) diff += 1;

			// TODO method flags other than access

			return 1 - diff;
		}
	);

	private static readonly AbstractClassifier argTypes = new("arg types", (methodA, methodB, env) => {
			return ClassifierUtil.CompareClassLists(getArgTypes(methodA), getArgTypes(methodB));
		}
	);

	private static List<TypeInstance> getArgTypes(MethodInstance method) {
		return [.. method.args.Select(param => param.paramType)];
	}

	private static readonly AbstractClassifier retType = new("ret type", (methodA, methodB, env) => {
			return ClassifierUtil.CheckPotentialEquality(methodA.returnType, methodB.returnType) ? 1 : 0;
		}
	);

	// private static readonly AbstractClassifier signature = new("signature", (methodA, methodB, env) => {
	// 		MethodSignature sigA = methodA.getSignature();
	// 		MethodSignature sigB = methodB.getSignature();

	// 		if (sigA == null && sigB == null) return 1;
	// 		if (sigA == null || sigB == null) return 0;

	// 		return sigA.isPotentiallyEqual(sigB) ? 1 : 0;
	// 	}
	// );

	private static readonly AbstractClassifier classRefs = new("class refs", (methodA, methodB, env) => {
			return ClassifierUtil.CompareClassSets(methodA.typeRefs, methodB.typeRefs, true);
		}
	);

	private static readonly AbstractClassifier stringConstants = new("string constants", (methodA, methodB, env) => {
			if (!CheckAsmNodes(methodA, methodB)) return CompareAsmNodes(methodA, methodB);

			return ClassifierUtil.CompareSets(methodA.strings, methodB.strings, false);
		}
	);

	private static readonly AbstractClassifier numericConstants = new("numeric constants", (methodA, methodB, env) => {
			if (!CheckAsmNodes(methodA, methodB)) return CompareAsmNodes(methodA, methodB);

			HashSet<int> intsA = [];
			HashSet<int> intsB = [];
			HashSet<long> longsA = [];
			HashSet<long> longsB = [];
			HashSet<float> floatsA = [];
			HashSet<float> floatsB = [];
			HashSet<double> doublesA = [];
			HashSet<double> doublesB = [];

			ClassifierUtil.ExtractNumbers(methodA.CecilMethod, intsA, longsA, floatsA, doublesA);
			ClassifierUtil.ExtractNumbers(methodB.CecilMethod, intsB, longsB, floatsB, doublesB);

			return (ClassifierUtil.CompareSets(intsA, intsB, false)
					+ ClassifierUtil.CompareSets(longsA, longsB, false)
					+ ClassifierUtil.CompareSets(floatsA, floatsB, false)
					+ ClassifierUtil.CompareSets(doublesA, doublesB, false)) / 4.0;
		}
	);

	private static readonly AbstractClassifier parentMethods = new("parent methods", (methodA, methodB, env) => {
			return ClassifierUtil.CompareMethodSets(methodA.parents, methodB.parents, true);
		}
	);

	private static readonly AbstractClassifier childMethods = new("child methods", (methodA, methodB, env) => {
			return ClassifierUtil.CompareMethodSets(methodA.children, methodB.children, true);
		}
	);

	private static readonly AbstractClassifier outReferences = new("out references", (methodA, methodB, env) => {
			return ClassifierUtil.CompareMethodSets(methodA.refsOut, methodB.refsOut, true);
		}
	);

	private static readonly AbstractClassifier inReferences = new("in references", (methodA, methodB, env) => {
			return ClassifierUtil.CompareMethodSets(methodA.refsIn, methodB.refsIn, true);
		}
	);

	private static readonly AbstractClassifier fieldReads = new("field reads", (methodA, methodB, env) => {
			return ClassifierUtil.CompareFieldSets(methodA.fieldReadRefs, methodB.fieldReadRefs, true);
		}
	);

	private static readonly AbstractClassifier fieldWrites = new("field writes", (methodA, methodB, env) => {
			return ClassifierUtil.CompareFieldSets(methodA.fieldWriteRefs, methodB.fieldWriteRefs, true);
		}
	);

	private static readonly AbstractClassifier position = new("position", (methodA, methodB, env) => {
			return ClassifierUtil.ClassifyPosition(methodA, methodB, method => method.Position, (m, idx) => m.ContainingType.methodsOrdered[idx], f => f.ContainingType.methodsOrdered);
		}
	);

	private static readonly AbstractClassifier code = new("code", (methodA, methodB, env) => {
			if (!CheckAsmNodes(methodA, methodB)) return CompareAsmNodes(methodA, methodB);

			return ClassifierUtil.CompareInsns(methodA, methodB);
		}
	);

	private static readonly AbstractClassifier inRefsBci = new("in refs (bci)", (methodA, methodB, env) => {
			string ownerA = methodA.ContainingType.GetName();
			string nameA = methodA.GetName();
			// TODO descs
			string idA = methodA.GetId();
			string ownerB = methodB.ContainingType.GetName();
			string nameB = methodB.GetName();
			string idB = methodB.GetId();

			int matched = 0;
			int mismatched = 0;

			foreach (MethodInstance src in methodA.refsIn) {
				if (src == methodA) continue;

				MethodInstance? dst = src.GetMatch();

				if (dst == null || !methodB.refsIn.Contains(dst)) {
					mismatched++;
					continue;
				}

				int[]? map = ClassifierUtil.MapInsns(src, dst!);
				if (map == null) continue;

				var ilA = src.CecilMethod!.Body.Instructions;
				var ilB = dst.CecilMethod!.Body.Instructions;

				for (int srcIdx = 0; srcIdx < map.Length; srcIdx++) {
					if (map[srcIdx] < 0) continue;

					var in_ = ilA[srcIdx];
					if (in_.Operand is not MethodReference) continue;

					if (!IsSameMethod((MethodReference) in_.Operand, ownerA, nameA, idA, methodA, env.EnvA)) continue;

					in_ = ilB[map[srcIdx]];
					if (in_.Operand is not MethodReference) continue;

					if (!IsSameMethod((MethodReference) in_.Operand, ownerB, nameB, idB, methodB, env.EnvB)) {
						mismatched++;
					} else {
						matched++;
					}
				}
			}

			if (matched == 0 && mismatched == 0) {
				return 1;
			} else {
				return (double) matched / (matched + mismatched);
			}
		}
	);

	private static bool IsSameMethod(MethodReference in_, string owner, string name, string id, MethodInstance method, LocalClassEnv env) {
		// string sOwner, sName, sDesc;
		// bool sItf;

		// if (in_.getType() == AbstractInsnNode.METHOD_INSN) {
		// 	MethodInsnNode min = (MethodInsnNode) in_;
		// 	sOwner = min.owner;
		// 	sName = min.name;
		// 	sDesc = min.id;
		// 	sItf = min.itf;
		// } else {
		// 	InvokeDynamicInsnNode din = (InvokeDynamicInsnNode) in_;
		// 	Handle impl = Util.getTargetHandle(din.bsm, din.bsmArgs);
		// 	if (impl == null) return false;

		// 	int tag = impl.getTag();
		// 	if (tag < Opcodes.H_INVOKEVIRTUAL || tag > Opcodes.H_INVOKEINTERFACE) return false;

		// 	sOwner = impl.getOwner();
		// 	sName = impl.getName();
		// 	sDesc = impl.getDesc();
		// 	sItf = Util.isCallToInterface(impl);
		// }

		TypeInstance? target;

		return in_.Name == name
				// && sDesc.equals(id)
				&& (in_.DeclaringType.Name == owner || (target = env.types!.GetValueOrDefault(in_.DeclaringType.Name, null)) != null && target.GetMethod(name, id) == method);
	}

	private static bool CheckAsmNodes(MethodInstance a, MethodInstance b) {
		return a.CecilMethod != null && b.CecilMethod != null;
	}

	private static double CompareAsmNodes(MethodInstance a, MethodInstance b) {
		return a.CecilMethod == null && b.CecilMethod == null ? 1 : 0;
	}

	public class AbstractClassifier(string name, Func<MethodInstance, MethodInstance, MatchingEnv, double> classifierFunc) : IClassifier<MethodInstance> {
		private readonly string name = name;
		public double weight; // probably shouldn't be public but I'm lazy and csharp nested types have different visibility rules so I can't just do private
		private readonly Func<MethodInstance, MethodInstance, MatchingEnv, double> classifierFunc = classifierFunc;

		public string GetName() {
			return name;
		}

		public double GetWeight() {
			return weight;
		}

		public double GetScore(MethodInstance a, MethodInstance b, MatchingEnv env) {
			return classifierFunc.Invoke(a, b, env);
		}
	}
}