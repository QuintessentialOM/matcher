using Mono.Cecil;

namespace Matcher.Matching.Classifier;

public class TypeClassifier {
	public static void Init() {
		// Normal subgroup
		AddClassifier(classTypeCheck, 20, TypeSubgroup.Normal);
		// AddClassifier(signature, 5); // <- this one seems to be generic params, and also compares superclass + interface signatures
		AddClassifier(hierarchyDepth, 1, TypeSubgroup.Normal);
		AddClassifier(parentClass, 4, TypeSubgroup.Normal);
		AddClassifier(childClasses, 3, TypeSubgroup.Normal);
		AddClassifier(interfaces, 3, TypeSubgroup.Normal);
		AddClassifier(implementers, 2, TypeSubgroup.Normal);
		AddClassifier(outerClass, 6, TypeSubgroup.Normal);
		// AddClassifier(position, 3, TypeSubgroup.Normal); // <- seems to actually make matching worse lol lmao
		AddClassifier(innerClasses, 5, TypeSubgroup.Normal);
		AddClassifier(methodCount, 3, TypeSubgroup.Normal);
		AddClassifier(fieldCount, 3, TypeSubgroup.Normal);
		AddClassifier(hierarchySiblings, 2, TypeSubgroup.Normal);
		AddClassifier(similarMethods, 10, TypeSubgroup.Normal);
		AddClassifier(outReferences, 6, TypeSubgroup.Normal);
		AddClassifier(inReferences, 6, TypeSubgroup.Normal);
		AddClassifier(stringConstants, 8, TypeSubgroup.Normal);
		AddClassifier(numericConstants, 6, TypeSubgroup.Normal);
		AddClassifier(methodOutReferences, 5, TypeSubgroup.Normal, ClassifierLevel.Intermediate, ClassifierLevel.Full, ClassifierLevel.Extra);
		AddClassifier(methodInReferences, 6, TypeSubgroup.Normal, ClassifierLevel.Intermediate, ClassifierLevel.Full, ClassifierLevel.Extra);
		AddClassifier(fieldReadReferences, 5, TypeSubgroup.Normal, ClassifierLevel.Intermediate, ClassifierLevel.Full, ClassifierLevel.Extra);
		AddClassifier(fieldWriteReferences, 5, TypeSubgroup.Normal, ClassifierLevel.Intermediate, ClassifierLevel.Full, ClassifierLevel.Extra);
		AddClassifier(membersFull, 10, TypeSubgroup.Normal, ClassifierLevel.Full, ClassifierLevel.Extra);
		AddClassifier(inRefsBci, 6, TypeSubgroup.Normal, ClassifierLevel.Extra);
		// Enum subgroup
		AddClassifier(outerClass, 6, TypeSubgroup.Enum);
		AddClassifier(position, 3, TypeSubgroup.Enum);
		AddClassifier(fieldCount, 3, TypeSubgroup.Enum);
		AddClassifier(hierarchySiblings, 2, TypeSubgroup.Enum);
		AddClassifier(outReferences, 6, TypeSubgroup.Enum);
		AddClassifier(inReferences, 6, TypeSubgroup.Enum);
		AddClassifier(numericConstants, 6, TypeSubgroup.Enum);
		AddClassifier(membersFull, 10, TypeSubgroup.Enum, ClassifierLevel.Full, ClassifierLevel.Extra);
		AddClassifier(inRefsBci, 6, TypeSubgroup.Enum, ClassifierLevel.Extra);
		// Delegate subgroup
		AddClassifier(outerClass, 6, TypeSubgroup.Delegate);
		// AddClassifier(position, 3, TypeSubgroup.Delegate);
		AddClassifier(methodCount, 3, TypeSubgroup.Delegate);
		AddClassifier(fieldCount, 3, TypeSubgroup.Delegate);
		AddClassifier(similarMethods, 10, TypeSubgroup.Delegate);
		AddClassifier(outReferences, 6, TypeSubgroup.Delegate);
		AddClassifier(inReferences, 6, TypeSubgroup.Delegate);
		AddClassifier(stringConstants, 8, TypeSubgroup.Delegate);
		AddClassifier(numericConstants, 6, TypeSubgroup.Delegate);
		AddClassifier(methodOutReferences, 5, TypeSubgroup.Delegate, ClassifierLevel.Intermediate, ClassifierLevel.Full, ClassifierLevel.Extra);
		AddClassifier(methodInReferences, 6, TypeSubgroup.Delegate, ClassifierLevel.Intermediate, ClassifierLevel.Full, ClassifierLevel.Extra);
		AddClassifier(fieldReadReferences, 5, TypeSubgroup.Delegate, ClassifierLevel.Intermediate, ClassifierLevel.Full, ClassifierLevel.Extra);
		AddClassifier(fieldWriteReferences, 5, TypeSubgroup.Delegate, ClassifierLevel.Intermediate, ClassifierLevel.Full, ClassifierLevel.Extra);
		AddClassifier(membersFull, 10, TypeSubgroup.Delegate, ClassifierLevel.Full, ClassifierLevel.Extra);
		AddClassifier(inRefsBci, 6, TypeSubgroup.Delegate, ClassifierLevel.Extra);
	}

	public static void AddClassifier(AbstractClassifier classifier, double weight, TypeSubgroup subgroup, params ClassifierLevel[] levels) {
		if (levels.Length == 0) levels = Enum.GetValues<ClassifierLevel>();

		classifier.weight = weight;

		foreach (ClassifierLevel level in levels) {
			if (!classifiers.ContainsKey((subgroup, level))) classifiers[(subgroup, level)] = [];
			classifiers[(subgroup, level)].Add(classifier);
			maxScore[(subgroup, level)] = GetMaxScore(level, subgroup) + weight;
		}
	}

	public static double GetMaxScore(ClassifierLevel level, TypeSubgroup subgroup) {
		return maxScore.GetValueOrDefault((subgroup, level), 0);
	}

	public static List<RankResult<TypeInstance>> Rank(TypeInstance src, TypeInstance[] dsts, ClassifierLevel level, MatchingEnv env, double maxMismatch, TypeSubgroup subgroup) {
		return ClassifierUtil.Rank(src, dsts, classifiers.GetValueOrDefault((subgroup, level), []), ClassifierUtil.CheckPotentialEquality, env, maxMismatch);
	}

	public static List<RankResult<TypeInstance>> RankParallel(TypeInstance src, TypeInstance[] dsts, ClassifierLevel level, MatchingEnv env, double maxMismatch, TypeSubgroup subgroup) {
		return ClassifierUtil.RankParallel(src, dsts, classifiers.GetValueOrDefault((subgroup, level), []), ClassifierUtil.CheckPotentialEquality, env, maxMismatch);
	}

	private static readonly Dictionary<(TypeSubgroup, ClassifierLevel), List<IClassifier<TypeInstance>>> classifiers = [];
	private static readonly Dictionary<(TypeSubgroup, ClassifierLevel), double> maxScore = [];

	private static readonly AbstractClassifier classTypeCheck = new("class type check", (clsA, clsB, env) => {
			// int mask = Opcodes.ACC_ENUM | Opcodes.ACC_INTERFACE | Opcodes.ACC_ANNOTATION | Opcodes.ACC_RECORD | Opcodes.ACC_ABSTRACT;
			// int resultA = clsA.getAccess() & mask;
			// int resultB = clsB.getAccess() & mask;

			// // assert int.bitCount(resultA) <= 3 && int.bitCount(resultB) <= 3;

			// return 1 - int.bitCount(resultA ^ resultB) / 5;
			int diff = 0;

			diff += clsA.CecilType.IsClass != clsB.CecilType.IsClass ? 1 : 0;
			diff += clsA.CecilType.IsInterface != clsB.CecilType.IsInterface ? 1 : 0;
			diff += clsA.CecilType.IsAbstract != clsB.CecilType.IsAbstract ? 1 : 0;
			// diff += clsA.CecilType.IsEnum != clsB.CecilType.IsEnum ? 1 : 0; // excluded because we special-case enums anyway
			diff += clsA.CecilType.IsValueType != clsB.CecilType.IsValueType ? 1 : 0;
			diff += clsA.CecilType.IsSealed != clsB.CecilType.IsSealed ? 1 : 0; // TODO maybe this one should be weighted less?

			return 1 - diff / 5.0;
		}
	);

	// private static readonly AbstractClassifier signature = new("signature", (clsA, clsB, env) => {
	// 		ClassSignature sigA = clsA.getSignature();
	// 		ClassSignature sigB = clsB.getSignature();

	// 		if (sigA == null && sigB == null) return 1;
	// 		if (sigA == null || sigB == null) return 0;

	// 		return sigA.isPotentiallyEqual(sigB) ? 1 : 0;
	// 	}
	// );

	private static readonly AbstractClassifier hierarchyDepth = new("hierarchy depth", (clsA, clsB, env) => {
			int countA = 0;
			int countB = 0;

			while (clsA.baseType != null) {
				clsA = clsA.baseType;
				countA++;
			}

			while (clsB.baseType != null) {
				clsB = clsB.baseType;
				countB++;
			}

			return ClassifierUtil.CompareCounts(countA, countB);
		}
	);

	private static readonly AbstractClassifier hierarchySiblings = new("hierarchy siblings", (clsA, clsB, env) => {
			return ClassifierUtil.CompareCounts(clsA.baseType?.childTypes.Count ?? 1, clsB.baseType?.childTypes.Count ?? 1);
		}
	);

	private static readonly AbstractClassifier parentClass = new("parent class", (clsA, clsB, env) => {
			if (clsA.baseType == null && clsB.baseType == null) return 1;
			if (clsA.baseType == null || clsB.baseType == null) return 0;

			return ClassifierUtil.CheckPotentialEquality(clsA.baseType, clsB.baseType) ? 1 : 0;
		}
	);

	private static readonly AbstractClassifier childClasses = new("child classes", (clsA, clsB, env) => {
			return ClassifierUtil.CompareClassSets(clsA.childTypes, clsB.childTypes, true);
		}
	);

	private static readonly AbstractClassifier interfaces = new("interfaces", (clsA, clsB, env) => {
			return ClassifierUtil.CompareClassSets(clsA.interfaces, clsB.interfaces, true);
		}
	);

	private static readonly AbstractClassifier implementers = new("implementers", (clsA, clsB, env) => {
			return ClassifierUtil.CompareClassSets(clsA.implementedBy, clsB.implementedBy, true);
		}
	);

	private static readonly AbstractClassifier outerClass = new("outer class", (clsA, clsB, env) => {
			TypeInstance? outerA = clsA.outerType;
			TypeInstance? outerB = clsB.outerType;

			if (outerA == null && outerB == null) return 1;
			if (outerA == null || outerB == null) return 0;

			return ClassifierUtil.CheckPotentialEquality(outerA, outerB) ? 1 : 0;
		}
	);

	private static readonly AbstractClassifier position = new("position", (clsA, clsB, env) => {
			if (clsA.position == -1 && clsB.position == -1) return 1;
			if (clsA.position == -1 || clsB.position == -1) return 0;
			return ClassifierUtil.ClassifyPosition(clsA, clsB, cls => cls.position, (f, idx) => f.outerType!.nestedTypes[idx], f => f.outerType!.nestedTypes);
		}
	);

	private static readonly AbstractClassifier innerClasses = new("inner classes", (clsA, clsB, env) => {
			List<TypeInstance> innerA = clsA.nestedTypes;
			List<TypeInstance> innerB = clsB.nestedTypes;

			if (innerA.Count == 0 && innerB.Count == 0) return 1;
			if (innerA.Count == 0 || innerB.Count == 0) return 0;

			return ClassifierUtil.CompareClassSets(innerA, innerB, true);
		}
	);

	private static readonly AbstractClassifier methodCount = new("method count", (clsA, clsB, env) => {
			return ClassifierUtil.CompareCounts(clsA.methodsById.Count, clsB.methodsById.Count);
		}
	);

	private static readonly AbstractClassifier fieldCount = new("field count", (clsA, clsB, env) => {
			return ClassifierUtil.CompareCounts(clsA.fieldsById.Count, clsB.fieldsById.Count);
		}
	);

	private static readonly AbstractClassifier similarMethods = new("similar methods", (clsA, clsB, env) => {
			if (clsA.methodsById.Count == 0 && clsB.methodsById.Count == 0) return 1;
			if (clsA.methodsById.Count == 0 || clsB.methodsById.Count == 0) return 0;

			HashSet<MethodInstance> methodsB = [.. clsB.methodsById.Values];
			double totalScore = 0;
			MethodInstance? bestMatch = null;
			double bestScore = 0;

			foreach (MethodInstance methodA in clsA.methodsById.Values) {
				{
					foreach (MethodInstance methodB in methodsB) {
						if (!ClassifierUtil.CheckPotentialEquality(methodA, methodB)) continue;
						if (!ClassifierUtil.CheckPotentialEquality(methodA.returnType, methodB.returnType)) continue;

						MethodParamInstance[] argsA = methodA.args;
						MethodParamInstance[] argsB = methodB.args;
						if (argsA.Length != argsB.Length) continue;

						for (int i = 0; i < argsA.Length; i++) {
							TypeInstance argA = argsA[i].paramType;
							TypeInstance argB = argsB[i].paramType;

							if (!ClassifierUtil.CheckPotentialEquality(argA, argB)) {
								goto mBLoop_continue;
							}
						}

						MethodDefinition asmNodeA = methodA.CecilMethod;
						MethodDefinition asmNodeB = methodB.CecilMethod;
						double score;

						if (asmNodeA == null || asmNodeB == null || asmNodeA.Body == null || asmNodeB.Body == null) {
							score = (asmNodeA == null || asmNodeA.Body == null) && (asmNodeB == null || asmNodeB.Body == null) ? 1 : 0;
						} else {
							score = ClassifierUtil.CompareCounts(asmNodeA.Body.Instructions.Count, asmNodeB.Body.Instructions.Count);
						}

						if (score > bestScore) {
							bestScore = score;
							bestMatch = methodB;
						}
						mBLoop_continue: {}
					}
				}

				if (bestMatch != null) {
					totalScore += bestScore;
					methodsB.Remove(bestMatch);
				}
			}

			return totalScore / Math.Max(clsA.methodsById.Count, clsB.methodsById.Count);
		}
	);

	private static readonly AbstractClassifier outReferences = new("out references", (clsA, clsB, env) => {
			HashSet<TypeInstance> refsA = GetOutRefs(clsA);
			HashSet<TypeInstance> refsB = GetOutRefs(clsB);

			return ClassifierUtil.CompareClassSets(refsA, refsB, false);
		}
	);

	private static HashSet<TypeInstance> GetOutRefs(TypeInstance cls) {
		HashSet<TypeInstance> ret = [];

		foreach (MethodInstance method in cls.methodsById.Values) {
			ret.UnionWith(method.typeRefs);
		}

		foreach (FieldInstance field in cls.fieldsById.Values) {
			ret.Add(field.fieldType);
		}

		return ret;
	}

	private static readonly AbstractClassifier inReferences = new("in references", (clsA, clsB, env) => {
			HashSet<TypeInstance> refsA = getInRefs(clsA);
			HashSet<TypeInstance> refsB = getInRefs(clsB);

			return ClassifierUtil.CompareClassSets(refsA, refsB, false);
		}
	);

	private static HashSet<TypeInstance> getInRefs(TypeInstance cls) {
		HashSet<TypeInstance> ret = [];

		foreach (MethodInstance method in cls.methodTypeRefs) {
			ret.Add(method.ContainingType);
		}

		foreach (FieldInstance field in cls.fieldTypeRefs) {
			ret.Add(field.ContainingType);
		}

		return ret;
	}

	private static readonly AbstractClassifier methodOutReferences = new("method out references", (clsA, clsB, env) => {
			HashSet<MethodInstance> refsA = getMethodOutRefs(clsA);
			HashSet<MethodInstance> refsB = getMethodOutRefs(clsB);

			return ClassifierUtil.CompareMethodSets(refsA, refsB, false);
		}
	);

	private static HashSet<MethodInstance> getMethodOutRefs(TypeInstance cls) {
		HashSet<MethodInstance> ret = [];

		foreach (MethodInstance method in cls.methodsById.Values) {
			ret.UnionWith(method.refsOut);
		}

		return ret;
	}

	private static readonly AbstractClassifier methodInReferences = new("method in references", (clsA, clsB, env) => {
			HashSet<MethodInstance> refsA = getMethodInRefs(clsA);
			HashSet<MethodInstance> refsB = getMethodInRefs(clsB);

			return ClassifierUtil.CompareMethodSets(refsA, refsB, false);
		}
	);

	private static HashSet<MethodInstance> getMethodInRefs(TypeInstance cls) {
		HashSet<MethodInstance> ret = [];

		foreach (MethodInstance method in cls.methodsById.Values) {
			ret.UnionWith(method.refsIn);
		}

		return ret;
	}

	private static readonly AbstractClassifier fieldReadReferences = new("field read references", (clsA, clsB, env) => {
			HashSet<FieldInstance> refsA = GetFieldReadRefs(clsA);
			HashSet<FieldInstance> refsB = GetFieldReadRefs(clsB);

			return ClassifierUtil.CompareFieldSets(refsA, refsB, false);
		}
	);

	private static HashSet<FieldInstance> GetFieldReadRefs(TypeInstance cls) {
		HashSet<FieldInstance> ret = [];

		foreach (MethodInstance method in cls.methodsById.Values) {
			ret.UnionWith(method.fieldReadRefs);
		}

		return ret;
	}

	private static readonly AbstractClassifier fieldWriteReferences = new("field write references", (clsA, clsB, env) => {
			HashSet<FieldInstance> refsA = GetFieldWriteRefs(clsA);
			HashSet<FieldInstance> refsB = GetFieldWriteRefs(clsB);

			return ClassifierUtil.CompareFieldSets(refsA, refsB, false);
		}
	);

	private static HashSet<FieldInstance> GetFieldWriteRefs(TypeInstance cls) {
		HashSet<FieldInstance> ret = [];

		foreach (MethodInstance method in cls.methodsById.Values) {
			ret.UnionWith(method.fieldWriteRefs);
		}

		return ret;
	}

	private static readonly AbstractClassifier stringConstants = new("string constants", (clsA, clsB, env) => {
			return ClassifierUtil.CompareSets(clsA.strings, clsB.strings, true);
		}
	);

	private static readonly AbstractClassifier numericConstants = new("numeric constants", (clsA, clsB, env) => {
			HashSet<int> intsA = [];
			HashSet<int> intsB = [];
			HashSet<long> longsA = [];
			HashSet<long> longsB = [];
			HashSet<float> floatsA = [];
			HashSet<float> floatsB = [];
			HashSet<double> doublesA = [];
			HashSet<double> doublesB = [];

			ExtractNumbers(clsA, intsA, longsA, floatsA, doublesA);
			ExtractNumbers(clsB, intsB, longsB, floatsB, doublesB);

			return (ClassifierUtil.CompareSets(intsA, intsB, false)
					+ ClassifierUtil.CompareSets(longsA, longsB, false)
					+ ClassifierUtil.CompareSets(floatsA, floatsB, false)
					+ ClassifierUtil.CompareSets(doublesA, doublesB, false)) / 4;
		}
	);

	private static readonly AbstractClassifier membersFull = new("members full", (clsA, clsB, env) => {
			double absThreshold = 0.8;
			double relThreshold = 0.08;
			ClassifierLevel level = ClassifierLevel.Full;
			double match = 0;

			if (clsA.methodsById.Count > 0 && clsB.methodsById.Count > 0) {
				double maxScore = MethodClassifier.GetMaxScore(level);

				foreach (MethodInstance method in clsA.methodsById.Values) {
					if (!method.IsMatchable()) continue;

					List<RankResult<MethodInstance>> ranking = MethodClassifier.Rank(method, [.. clsB.methodsById.Values], level, env);
					if (ClassifierUtil.CheckRank(ranking, absThreshold, relThreshold, maxScore)) match += ClassifierUtil.GetScore(ranking[0].Score, maxScore);
				}
			}

			if (clsA.fieldsById.Count > 0 && clsB.fieldsById.Count > 0) {
				double maxScore = FieldClassifier.GetMaxScore(level);

				foreach (FieldInstance field in clsA.fieldsById.Values) {
					if (!field.IsMatchable()) continue;

					List<RankResult<FieldInstance>> ranking = FieldClassifier.Rank(field, [.. clsB.fieldsById.Values], level, env);
					if (ClassifierUtil.CheckRank(ranking, absThreshold, relThreshold, maxScore)) match += ClassifierUtil.GetScore(ranking[0].Score, maxScore);
				}
			}

			int methods = Math.Max(clsA.methodsById.Count, clsB.methodsById.Count);
			int fields = Math.Max(clsA.fieldsById.Count, clsB.fieldsById.Count);

			if (methods == 0 && fields == 0) {
				return 1;
			} else {
				// assert match <= methods + fields;

				return match / (methods + fields);
			}
		}
	);

	private static readonly AbstractClassifier inRefsBci = new("in refs (bci)", (clsA, clsB, env) => {
			int matched = 0;
			int mismatched = 0;

			foreach (MethodInstance src in clsA.methodTypeRefs) {
				if (src.ContainingType == clsA) continue;

				MethodInstance? dst = src.GetMatch();

				if (dst == null || !clsB.methodTypeRefs.Contains(dst)) {
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

					MethodReference min = (MethodReference) in_.Operand;
					TypeInstance? owner = env.EnvA.types!.GetValueOrDefault(min.DeclaringType.Name, null);

					if (owner != clsA) continue;

					in_ = ilB[map[srcIdx]];
					if (in_.Operand is not MethodReference) continue; // shouldn't happen I think?
					min = (MethodReference) in_.Operand;
					owner = env.EnvB.types!.GetValueOrDefault(min.DeclaringType.Name, null);

					if (owner != clsB) {
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

	private static void ExtractNumbers(TypeInstance cls, ISet<int> ints, ISet<long> longs, ISet<float> floats, ISet<double> doubles) {
		foreach (MethodInstance method in cls.methodsById.Values) {
			if (method.CecilMethod == null) continue;

			ClassifierUtil.ExtractNumbers(method.CecilMethod, ints, longs, floats, doubles);
		}

		// foreach (FieldInstance field in cls.fieldsById.Values) {
		// 	FieldNode asmNode = field.getAsmNode();
		// 	if (asmNode == null) continue;

		// 	ClassifierUtil.handleNumberValue(asmNode.value, ints, longs, floats, doubles);
		// }
	}

	public class AbstractClassifier(string name, Func<TypeInstance, TypeInstance, MatchingEnv, double> classifierFunc) : IClassifier<TypeInstance> {
		private readonly string name = name;
		public double weight; // probably shouldn't be public but I'm lazy and csharp nested types have different visibility rules so I can't just do private
		private readonly Func<TypeInstance, TypeInstance, MatchingEnv, double> classifierFunc = classifierFunc;

		public string GetName() {
			return name;
		}

		public double GetWeight() {
			return weight;
		}

		public double GetScore(TypeInstance a, TypeInstance b, MatchingEnv env) {
			return classifierFunc.Invoke(a, b, env);
		}
	}
}
