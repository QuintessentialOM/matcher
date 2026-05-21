namespace Matcher.Matching.Classifier;

public class TypeClassifier {
	public static void init() {
		addClassifier(classTypeCheck, 20);
		// addClassifier(signature, 5); // <- this one seems to be generic params, and also compares superclass + interface signatures
		addClassifier(hierarchyDepth, 1);
		addClassifier(parentClass, 4);
		addClassifier(childClasses, 3);
		addClassifier(interfaces, 3);
		addClassifier(implementers, 2);
		addClassifier(outerClass, 6);
		addClassifier(innerClasses, 5);
		addClassifier(methodCount, 3);
		addClassifier(fieldCount, 3);
		addClassifier(hierarchySiblings, 2);
		// addClassifier(similarMethods, 10);
		// addClassifier(outReferences, 6);
		// addClassifier(inReferences, 6);
		addClassifier(stringConstants, 8);
		// addClassifier(numericConstants, 6);
		// addClassifier(methodOutReferences, 5, ClassifierLevel.Intermediate, ClassifierLevel.Full, ClassifierLevel.Extra);
		// addClassifier(methodInReferences, 6, ClassifierLevel.Intermediate, ClassifierLevel.Full, ClassifierLevel.Extra);
		// addClassifier(fieldReadReferences, 5, ClassifierLevel.Intermediate, ClassifierLevel.Full, ClassifierLevel.Extra);
		// addClassifier(fieldWriteReferences, 5, ClassifierLevel.Intermediate, ClassifierLevel.Full, ClassifierLevel.Extra);
		// addClassifier(membersFull, 10, ClassifierLevel.Full, ClassifierLevel.Extra);
		// addClassifier(inRefsBci, 6, ClassifierLevel.Extra);
	}

	public static void addClassifier(AbstractClassifier classifier, double weight, params ClassifierLevel[] levels) {
		if (levels.Length == 0) levels = Enum.GetValues<ClassifierLevel>();

		classifier.weight = weight;

		foreach (ClassifierLevel level in levels) {
			if (!classifiers.ContainsKey(level)) classifiers[level] = [];
			classifiers[level].Add(classifier);
			maxScore[level] = getMaxScore(level) + weight;
		}
	}

	public static double getMaxScore(ClassifierLevel level) {
		return maxScore.GetValueOrDefault(level, 0);
	}

	public static List<RankResult<TypeInstance>> rank(TypeInstance src, TypeInstance[] dsts, ClassifierLevel level, MatchingEnv env, double maxMismatch) {
		return ClassifierUtil.rank(src, dsts, classifiers.GetValueOrDefault(level, []), ClassifierUtil.checkPotentialEquality, env, maxMismatch);
	}

	public static List<RankResult<TypeInstance>> rankParallel(TypeInstance src, TypeInstance[] dsts, ClassifierLevel level, MatchingEnv env, double maxMismatch) {
		return ClassifierUtil.rankParallel(src, dsts, classifiers.GetValueOrDefault(level, []), ClassifierUtil.checkPotentialEquality, env, maxMismatch);
	}

	private static readonly Dictionary<ClassifierLevel, List<IClassifier<TypeInstance>>> classifiers = new();
	private static readonly Dictionary<ClassifierLevel, double> maxScore = new();

	private static AbstractClassifier classTypeCheck = new AbstractClassifier("class type check", (TypeInstance clsA, TypeInstance clsB, MatchingEnv env) => {
			// int mask = Opcodes.ACC_ENUM | Opcodes.ACC_INTERFACE | Opcodes.ACC_ANNOTATION | Opcodes.ACC_RECORD | Opcodes.ACC_ABSTRACT;
			// int resultA = clsA.getAccess() & mask;
			// int resultB = clsB.getAccess() & mask;

			// // assert int.bitCount(resultA) <= 3 && int.bitCount(resultB) <= 3;

			// return 1 - int.bitCount(resultA ^ resultB) / 5;
			int diff = 0;

			diff += clsA.cecilType.IsClass != clsB.cecilType.IsClass ? 1 : 0;
			diff += clsA.cecilType.IsInterface != clsB.cecilType.IsInterface ? 1 : 0;
			diff += clsA.cecilType.IsAbstract != clsB.cecilType.IsAbstract ? 1 : 0;
			diff += clsA.cecilType.IsEnum != clsB.cecilType.IsEnum ? 1 : 0;
			diff += clsA.cecilType.IsSealed != clsB.cecilType.IsSealed ? 1 : 0; // TODO maybe this one should be weighted less?

			return 1 - diff / 5.0;
		}
	);

	// private static AbstractClassifier signature = new AbstractClassifier("signature", (TypeInstance clsA, TypeInstance clsB, MatchingEnv env) => {
	// 		ClassSignature sigA = clsA.getSignature();
	// 		ClassSignature sigB = clsB.getSignature();

	// 		if (sigA == null && sigB == null) return 1;
	// 		if (sigA == null || sigB == null) return 0;

	// 		return sigA.isPotentiallyEqual(sigB) ? 1 : 0;
	// 	}
	// );

	private static AbstractClassifier hierarchyDepth = new AbstractClassifier("hierarchy depth", (TypeInstance clsA, TypeInstance clsB, MatchingEnv env) => {
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

			return ClassifierUtil.compareCounts(countA, countB);
		}
	);

	private static AbstractClassifier hierarchySiblings = new AbstractClassifier("hierarchy siblings", (TypeInstance clsA, TypeInstance clsB, MatchingEnv env) => {
			return ClassifierUtil.compareCounts(clsA.baseType?.childTypes.Count ?? 1, clsB.baseType?.childTypes.Count ?? 1);
		}
	);

	private static AbstractClassifier parentClass = new AbstractClassifier("parent class", (TypeInstance clsA, TypeInstance clsB, MatchingEnv env) => {
			if (clsA.baseType == null && clsB.baseType == null) return 1;
			if (clsA.baseType == null || clsB.baseType == null) return 0;

			return ClassifierUtil.checkPotentialEquality(clsA.baseType, clsB.baseType) ? 1 : 0;
		}
	);

	private static AbstractClassifier childClasses = new AbstractClassifier("child classes", (TypeInstance clsA, TypeInstance clsB, MatchingEnv env) => {
			return ClassifierUtil.compareClassSets(clsA.childTypes, clsB.childTypes, true);
		}
	);

	private static AbstractClassifier interfaces = new AbstractClassifier("interfaces", (TypeInstance clsA, TypeInstance clsB, MatchingEnv env) => {
			return ClassifierUtil.compareClassSets(clsA.interfaces, clsB.interfaces, true);
		}
	);

	private static AbstractClassifier implementers = new AbstractClassifier("implementers", (TypeInstance clsA, TypeInstance clsB, MatchingEnv env) => {
			return ClassifierUtil.compareClassSets(clsA.implementedBy, clsB.implementedBy, true);
		}
	);

	private static AbstractClassifier outerClass = new AbstractClassifier("outer class", (TypeInstance clsA, TypeInstance clsB, MatchingEnv env) => {
			TypeInstance? outerA = clsA.outerType;
			TypeInstance? outerB = clsB.outerType;

			if (outerA == null && outerB == null) return 1;
			if (outerA == null || outerB == null) return 0;

			return ClassifierUtil.checkPotentialEquality(outerA, outerB) ? 1 : 0;
		}
	);

	private static AbstractClassifier innerClasses = new AbstractClassifier("inner classes", (TypeInstance clsA, TypeInstance clsB, MatchingEnv env) => {
			List<TypeInstance> innerA = clsA.nestedTypes;
			List<TypeInstance> innerB = clsB.nestedTypes;

			if (innerA.Count == 0 && innerB.Count == 0) return 1;
			if (innerA.Count == 0 || innerB.Count == 0) return 0;

			return ClassifierUtil.compareClassSets(innerA, innerB, true);
		}
	);

	private static AbstractClassifier methodCount = new AbstractClassifier("method count", (TypeInstance clsA, TypeInstance clsB, MatchingEnv env) => {
			return ClassifierUtil.compareCounts(clsA.methodsById.Count, clsB.methodsById.Count);
		}
	);

	private static AbstractClassifier fieldCount = new AbstractClassifier("field count", (TypeInstance clsA, TypeInstance clsB, MatchingEnv env) => {
			return ClassifierUtil.compareCounts(clsA.fieldsById.Count, clsB.fieldsById.Count);
		}
	);

	// private static AbstractClassifier similarMethods = new AbstractClassifier("similar methods", (TypeInstance clsA, TypeInstance clsB, MatchingEnv env) => {
	// 		if (clsA.methodsById.Count == 0 && clsB.methodsById.Count == 0) return 1;
	// 		if (clsA.methodsById.Count == 0 || clsB.methodsById.Count == 0) return 0;

	// 		ISet<MethodInstance> methodsB = Util.newIdentityHashSet(Arrays.asList(clsB.methodsById.Values));
	// 		double totalScore = 0;
	// 		MethodInstance bestMatch = null;
	// 		double bestScore = 0;

	// 		for (MethodInstance methodA : clsA.methodsById.Values) {
	// 			{
	// 				mBLoop: for (MethodInstance methodB : methodsB) {
	// 					if (!ClassifierUtil.checkPotentialEquality(methodA, methodB)) continue;
	// 					if (!ClassifierUtil.checkPotentialEquality(methodA.getRetType(), methodB.getRetType())) continue;

	// 					MethodVarInstance[] argsA = methodA.getArgs();
	// 					MethodVarInstance[] argsB = methodB.getArgs();
	// 					if (argsA.Length != argsB.Length) continue;

	// 					for (int i = 0; i < argsA.Length; i++) {
	// 						TypeInstance argA = argsA[i].getType();
	// 						TypeInstance argB = argsB[i].getType();

	// 						if (!ClassifierUtil.checkPotentialEquality(argA, argB)) {
	// 							continue mBLoop;
	// 						}
	// 					}

	// 					MethodNode asmNodeA = methodA.getAsmNode();
	// 					MethodNode asmNodeB = methodB.getAsmNode();
	// 					double score;

	// 					if (asmNodeA == null || asmNodeB == null) {
	// 						score = asmNodeA == null && asmNodeB == null ? 1 : 0;
	// 					} else {
	// 						score = ClassifierUtil.compareCounts(asmNodeA.instructions.size(), asmNodeB.instructions.size());
	// 					}

	// 					if (score > bestScore) {
	// 						bestScore = score;
	// 						bestMatch = methodB;
	// 					}
	// 				}
	// 			}

	// 			if (bestMatch != null) {
	// 				totalScore += bestScore;
	// 				methodsB.remove(bestMatch);
	// 			}
	// 		}

	// 		return totalScore / Math.Max(clsA.methodsById.Count, clsB.methodsById.Count);
	// 	}
	// );

	// private static AbstractClassifier outReferences = new AbstractClassifier("out references", (TypeInstance clsA, TypeInstance clsB, MatchingEnv env) => {
	// 		ISet<TypeInstance> refsA = getOutRefs(clsA);
	// 		ISet<TypeInstance> refsB = getOutRefs(clsB);

	// 		return ClassifierUtil.compareClassSets(refsA, refsB, false);
	// 	}
	// );

	// private static ISet<TypeInstance> getOutRefs(TypeInstance cls) {
	// 	ISet<TypeInstance> ret = Util.newIdentityHashSet();

	// 	foreach (MethodInstance method in cls.methodsById.Values) {
	// 		ret.addAll(method.getClassRefs());
	// 	}

	// 	foreach (FieldInstance field in cls.fieldsById.Values) {
	// 		ret.add(field.getType());
	// 	}

	// 	return ret;
	// }

	// private static AbstractClassifier inReferences = new AbstractClassifier("in references", (TypeInstance clsA, TypeInstance clsB, MatchingEnv env) => {
	// 		ISet<TypeInstance> refsA = getInRefs(clsA);
	// 		ISet<TypeInstance> refsB = getInRefs(clsB);

	// 		return ClassifierUtil.compareClassSets(refsA, refsB, false);
	// 	}
	// );

	// private static ISet<TypeInstance> getInRefs(TypeInstance cls) {
	// 	ISet<TypeInstance> ret = Util.newIdentityHashSet();

	// 	foreach (MethodInstance method in cls.getMethodTypeRefs()) {
	// 		ret.add(method.getCls());
	// 	}

	// 	foreach (FieldInstance field in cls.getFieldTypeRefs()) {
	// 		ret.add(field.getCls());
	// 	}

	// 	return ret;
	// }

	// private static AbstractClassifier methodOutReferences = new AbstractClassifier("method out references", (TypeInstance clsA, TypeInstance clsB, MatchingEnv env) => {
	// 		ISet<MethodInstance> refsA = getMethodOutRefs(clsA);
	// 		ISet<MethodInstance> refsB = getMethodOutRefs(clsB);

	// 		return ClassifierUtil.compareMethodSets(refsA, refsB, false);
	// 	}
	// );

	// private static ISet<MethodInstance> getMethodOutRefs(TypeInstance cls) {
	// 	ISet<MethodInstance> ret = Util.newIdentityHashSet();

	// 	foreach (MethodInstance method in cls.methodsById.Values) {
	// 		ret.addAll(method.getRefsOut());
	// 	}

	// 	return ret;
	// }

	// private static AbstractClassifier methodInReferences = new AbstractClassifier("method in references", (TypeInstance clsA, TypeInstance clsB, MatchingEnv env) => {
	// 		ISet<MethodInstance> refsA = getMethodInRefs(clsA);
	// 		ISet<MethodInstance> refsB = getMethodInRefs(clsB);

	// 		return ClassifierUtil.compareMethodSets(refsA, refsB, false);
	// 	}
	// );

	// private static ISet<MethodInstance> getMethodInRefs(TypeInstance cls) {
	// 	ISet<MethodInstance> ret = Util.newIdentityHashSet();

	// 	foreach (MethodInstance method in cls.methodsById.Values) {
	// 		ret.addAll(method.getRefsIn());
	// 	}

	// 	return ret;
	// }

	// private static AbstractClassifier fieldReadReferences = new AbstractClassifier("field read references", (TypeInstance clsA, TypeInstance clsB, MatchingEnv env) => {
	// 		ISet<FieldInstance> refsA = getFieldReadRefs(clsA);
	// 		ISet<FieldInstance> refsB = getFieldReadRefs(clsB);

	// 		return ClassifierUtil.compareFieldSets(refsA, refsB, false);
	// 	}
	// );

	// private static ISet<FieldInstance> getFieldReadRefs(TypeInstance cls) {
	// 	ISet<FieldInstance> ret = Util.newIdentityHashSet();

	// 	foreach (MethodInstance method in cls.methodsById.Values) {
	// 		ret.addAll(method.getFieldReadRefs());
	// 	}

	// 	return ret;
	// }

	// private static AbstractClassifier fieldWriteReferences = new AbstractClassifier("field write references", (TypeInstance clsA, TypeInstance clsB, MatchingEnv env) => {
	// 		ISet<FieldInstance> refsA = getFieldWriteRefs(clsA);
	// 		ISet<FieldInstance> refsB = getFieldWriteRefs(clsB);

	// 		return ClassifierUtil.compareFieldSets(refsA, refsB, false);
	// 	}
	// );

	// private static ISet<FieldInstance> getFieldWriteRefs(TypeInstance cls) {
	// 	ISet<FieldInstance> ret = Util.newIdentityHashSet();

	// 	foreach (MethodInstance method in cls.methodsById.Values) {
	// 		ret.addAll(method.getFieldWriteRefs());
	// 	}

	// 	return ret;
	// }

	private static AbstractClassifier stringConstants = new AbstractClassifier("string constants", (TypeInstance clsA, TypeInstance clsB, MatchingEnv env) => {
			return ClassifierUtil.compareSets(clsA.strings, clsB.strings, true);
		}
	);

	// private static AbstractClassifier numericConstants = new AbstractClassifier("numeric constants", (TypeInstance clsA, TypeInstance clsB, MatchingEnv env) => {
	// 		HashSet<int> intsA = new();
	// 		HashSet<int> intsB = new();
	// 		HashSet<long> longsA = new();
	// 		HashSet<long> longsB = new();
	// 		HashSet<float> floatsA = new();
	// 		HashSet<float> floatsB = new();
	// 		HashSet<double> doublesA = new();
	// 		HashSet<double> doublesB = new();

	// 		extractNumbers(clsA, intsA, longsA, floatsA, doublesA);
	// 		extractNumbers(clsB, intsB, longsB, floatsB, doublesB);

	// 		return (ClassifierUtil.compareSets(intsA, intsB, false)
	// 				+ ClassifierUtil.compareSets(longsA, longsB, false)
	// 				+ ClassifierUtil.compareSets(floatsA, floatsB, false)
	// 				+ ClassifierUtil.compareSets(doublesA, doublesB, false)) / 4;
	// 	}
	// );

	// private static AbstractClassifier membersFull = new AbstractClassifier("members full", (TypeInstance clsA, TypeInstance clsB, MatchingEnv env) => {
	// 		/*if (clsA.getName().equals("agl") && clsB.getName().equals("aht")) {
	// 			Matcher.LOGGER.info();
	// 		}*/

	// 		// TODO was a "0." stripped here?
	// 		double absThreshold = 08;
	// 		double relThreshold = 008;
	// 		ClassifierLevel level = ClassifierLevel.Full;
	// 		double match = 0;

	// 		if (clsA.methodsById.Count > 0 && clsB.methodsById.Count > 0) {
	// 			double maxScore = MethodClassifier.getMaxScore(level);

	// 			foreach (MethodInstance method in clsA.methodsById.Values) {
	// 				if (!method.isMatchable()) continue;

	// 				List<RankResult<MethodInstance>> ranking = MethodClassifier.rank(method, clsB.methodsById.Values, level, env);
	// 				if (ClassifierUtil.checkRank(ranking, absThreshold, relThreshold, maxScore)) match += ClassifierUtil.getScore(ranking.get(0).getScore(), maxScore);
	// 			}
	// 		}

	// 		if (clsA.fieldsById.Count > 0 && clsB.fieldsById.Count > 0) {
	// 			double maxScore = FieldClassifier.getMaxScore(level);

	// 			foreach (FieldInstance field in clsA.fieldsById.Values) {
	// 				if (!field.isMatchable()) continue;

	// 				List<RankResult<FieldInstance>> ranking = FieldClassifier.rank(field, clsB.fieldsById.Values, level, env);
	// 				if (ClassifierUtil.checkRank(ranking, absThreshold, relThreshold, maxScore)) match += ClassifierUtil.getScore(ranking.get(0).getScore(), maxScore);
	// 			}
	// 		}

	// 		int methods = Math.Max(clsA.methodsById.Count, clsB.methodsById.Count);
	// 		int fields = Math.Max(clsA.fieldsById.Count, clsB.fieldsById.Count);

	// 		if (methods == 0 && fields == 0) {
	// 			return 1;
	// 		} else {
	// 			// assert match <= methods + fields;

	// 			return match / (methods + fields);
	// 		}
	// 	}
	// );

	// private static AbstractClassifier inRefsBci = new AbstractClassifier("in refs (bci)", (TypeInstance clsA, TypeInstance clsB, MatchingEnv env) => {
	// 		int matched = 0;
	// 		int mismatched = 0;

	// 		foreach (MethodInstance src in clsA.getMethodTypeRefs()) {
	// 			if (src.getCls() == clsA) continue;

	// 			MethodInstance? dst = src.getMatch();

	// 			if (dst == null || !clsB.getMethodTypeRefs().contains(dst)) {
	// 				mismatched++;
	// 				continue;
	// 			}

	// 			int[]? map = ClassifierUtil.mapInsns(src, dst!);
	// 			if (map == null) continue;

	// 			InsnList ilA = src.getAsmNode().instructions;
	// 			InsnList ilB = dst!.getAsmNode().instructions;

	// 			for (int srcIdx = 0; srcIdx < map.Length; srcIdx++) {
	// 				if (map[srcIdx] < 0) continue;

	// 				AbstractInsnNode in_ = ilA.get(srcIdx);
	// 				if (in_.getType() != AbstractInsnNode.METHOD_INSN) continue;

	// 				MethodInsnNode min = (MethodInsnNode) in_;
	// 				TypeInstance owner = env.getClsByNameA(min.owner);

	// 				if (owner != clsA) continue;

	// 				in_ = ilB.get(map[srcIdx]);
	// 				min = (MethodInsnNode) in_;
	// 				owner = env.getClsByNameB(min.owner);

	// 				if (owner != clsB) {
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

	// private static void extractNumbers(TypeInstance cls, ISet<int> ints, ISet<long> longs, ISet<float> floats, ISet<double> doubles) {
	// 	foreach (MethodInstance method in cls.methodsById.Values) {
	// 		MethodNode asmNode = method.getAsmNode();
	// 		if (asmNode == null) continue;

	// 		ClassifierUtil.extractNumbers(asmNode, ints, longs, floats, doubles);
	// 	}

	// 	foreach (FieldInstance field in cls.fieldsById.Values) {
	// 		FieldNode asmNode = field.getAsmNode();
	// 		if (asmNode == null) continue;

	// 		ClassifierUtil.handleNumberValue(asmNode.value, ints, longs, floats, doubles);
	// 	}
	// }

	public class AbstractClassifier : IClassifier<TypeInstance> {
		private readonly string name;
		public double weight; // probably shouldn't be public but I'm lazy and csharp nested types have different visibility rules so I can't just do private
		private Func<TypeInstance, TypeInstance, MatchingEnv, double> classifierFunc;

		public AbstractClassifier(string name, Func<TypeInstance, TypeInstance, MatchingEnv, double> classifierFunc) {
			this.name = name;
			this.classifierFunc = classifierFunc;
		}

		public String getName() {
			return name;
		}

		public double getWeight() {
			return weight;
		}

		public double getScore(TypeInstance a, TypeInstance b, MatchingEnv env) {
			return classifierFunc.Invoke(a, b, env);
		}
	}
}
