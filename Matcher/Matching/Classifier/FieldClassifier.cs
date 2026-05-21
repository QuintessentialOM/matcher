namespace Matcher.Matching.Classifier;

public class FieldClassifier {
	public static void init() {
		addClassifier(fieldStaticCheck, 10);
		addClassifier(accessFlags, 4);
		addClassifier(type, 10);
		// addClassifier(signature, 5);
		// addClassifier(readReferences, 6);
		// addClassifier(writeReferences, 6);
		// addClassifier(position, 3);
		// addClassifier(initValue, 7);
		// addClassifier(initStrings, 8);
		// addClassifier(initCode, 10, ClassifierLevel.Intermediate, ClassifierLevel.Full, ClassifierLevel.Extra);
		// addClassifier(readRefsBci, 6, ClassifierLevel.Extra);
		// addClassifier(writeRefsBci, 6, ClassifierLevel.Extra);
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

	public static List<RankResult<FieldInstance>> rank(FieldInstance src, FieldInstance[] dsts, ClassifierLevel level, MatchingEnv env) {
		return rank(src, dsts, level, env, double.PositiveInfinity);
	}

	public static List<RankResult<FieldInstance>> rank(FieldInstance src, FieldInstance[] dsts, ClassifierLevel level, MatchingEnv env, double maxMismatch) {
		return ClassifierUtil.rank(src, dsts, classifiers.GetValueOrDefault(level, []), ClassifierUtil.checkPotentialEquality, env, maxMismatch);
	}

	private static readonly Dictionary<ClassifierLevel, List<IClassifier<FieldInstance>>> classifiers = new();
	private static readonly Dictionary<ClassifierLevel, double> maxScore = new();

	private static AbstractClassifier fieldStaticCheck = new AbstractClassifier("field static check", (FieldInstance fieldA, FieldInstance fieldB, MatchingEnv env) => {
			if (!checkAsmNodes(fieldA, fieldB)) return compareAsmNodes(fieldA, fieldB);

			return fieldA.cecilField.IsStatic == fieldB.cecilField.IsStatic ? 1 : 0;
		}
	);

	private static AbstractClassifier accessFlags = new AbstractClassifier("access flags", (FieldInstance fieldA, FieldInstance fieldB, MatchingEnv env) => {
			if (!checkAsmNodes(fieldA, fieldB)) return compareAsmNodes(fieldA, fieldB);

			// int mask = (Opcodes.ACC_PUBLIC | Opcodes.ACC_PROTECTED | Opcodes.ACC_PRIVATE) | Opcodes.ACC_FINAL | Opcodes.ACC_VOLATILE | Opcodes.ACC_TRANSIENT | Opcodes.ACC_SYNTHETIC;
			// int resultA = fieldA.getAsmNode().access & mask;
			// int resultB = fieldB.getAsmNode().access & mask;

			// return 1 - Integer.bitCount(resultA ^ resultB) / 6;

			int diff = 0;

			bool hasSameAccess = (fieldA.cecilField.IsPublic == fieldB.cecilField.IsPublic)
				&& (fieldA.cecilField.IsFamilyOrAssembly == fieldB.cecilField.IsFamilyOrAssembly)
				&& (fieldA.cecilField.IsFamily == fieldB.cecilField.IsFamily)
				&& (fieldA.cecilField.IsFamilyAndAssembly == fieldB.cecilField.IsFamilyAndAssembly)
				&& (fieldA.cecilField.IsAssembly == fieldB.cecilField.IsAssembly)
				&& (fieldA.cecilField.IsPrivate == fieldB.cecilField.IsPrivate);

			if (!hasSameAccess) diff += 1;

			// TODO field flags other than access

			return 1 - diff;
		}
	);

	private static AbstractClassifier type = new AbstractClassifier("types", (FieldInstance fieldA, FieldInstance fieldB, MatchingEnv env) => {
			return ClassifierUtil.checkPotentialEquality(fieldA.containingType, fieldB.containingType) ? 1 : 0;
		}
	);

	// private static AbstractClassifier signature = new AbstractClassifier("signature", (FieldInstance fieldA, FieldInstance fieldB, MatchingEnv env) => {
	// 		FieldSignature sigA = fieldA.getSignature();
	// 		FieldSignature sigB = fieldB.getSignature();

	// 		if (sigA == null && sigB == null) return 1;
	// 		if (sigA == null || sigB == null) return 0;

	// 		return sigA.isPotentiallyEqual(sigB) ? 1 : 0;
	// 	}
	// );

	// private static AbstractClassifier readReferences = new AbstractClassifier("read references", (FieldInstance fieldA, FieldInstance fieldB, MatchingEnv env) => {
	// 		return ClassifierUtil.compareMethodSets(fieldA.getReadRefs(), fieldB.getReadRefs(), true);
	// 	}
	// );

	// private static AbstractClassifier writeReferences = new AbstractClassifier("write references", (FieldInstance fieldA, FieldInstance fieldB, MatchingEnv env) => {
	// 		return ClassifierUtil.compareMethodSets(fieldA.getWriteRefs(), fieldB.getWriteRefs(), true);
	// 	}
	// );

	// private static AbstractClassifier position = new AbstractClassifier("position", (FieldInstance fieldA, FieldInstance fieldB, MatchingEnv env) => {
	// 		/*if (fieldA.position == fieldB.position) return 1;

	// 		double relPosA = ClassifierUtil.getRelativePosition(fieldA.position, fieldA.cls.fields.size());
	// 		double relPosB = ClassifierUtil.getRelativePosition(fieldB.position, fieldB.cls.fields.size());

	// 		return 1 - Math.abs(relPosA - relPosB);*/
	// 		return ClassifierUtil.classifyPosition(fieldA, fieldB, MemberInstance::getPosition, (f, idx) => f.containingType.getField(idx), f => f.containingType.getFields());
	// 	}
	// );

	// private static AbstractClassifier initValue = new AbstractClassifier("init value", (FieldInstance fieldA, FieldInstance fieldB, MatchingEnv env) => {
	// 		if (!checkAsmNodes(fieldA, fieldB)) return compareAsmNodes(fieldA, fieldB);

	// 		Object valA = fieldA.getAsmNode().value;
	// 		Object valB = fieldB.getAsmNode().value;

	// 		if (valA == null && valB == null) return 1;
	// 		if (valA == null || valB == null) return 0;

	// 		return valA.equals(valB) ? 1 : 0;
	// 	}
	// );

	// private static AbstractClassifier initStrings = new AbstractClassifier("init strings", (FieldInstance fieldA, FieldInstance fieldB, MatchingEnv env) => {
	// 		List<AbstractInsnNode> initA = fieldA.getInitializer();
	// 		List<AbstractInsnNode> initB = fieldB.getInitializer();

	// 		if (initA == null && initB == null) return 1;
	// 		if (initA == null || initB == null) return 0;

	// 		Set<String> stringsA = new HashSet<>();
	// 		ClassifierUtil.extractStrings(initA, stringsA);
	// 		Set<String> stringsB = new HashSet<>();
	// 		ClassifierUtil.extractStrings(initB, stringsB);

	// 		return ClassifierUtil.compareSets(stringsA, stringsB, false);
	// 	}
	// );

	// private static AbstractClassifier initCode = new AbstractClassifier("init code", (FieldInstance fieldA, FieldInstance fieldB, MatchingEnv env) => {
	// 		List<AbstractInsnNode> initA = fieldA.getInitializer();
	// 		List<AbstractInsnNode> initB = fieldB.getInitializer();

	// 		if (initA == null && initB == null) return 1;
	// 		if (initA == null || initB == null) return 0;

	// 		return ClassifierUtil.compareInsns(initA, initB, env);
	// 	}
	// );

	// private static AbstractClassifier readRefsBci = new AbstractClassifier("read refs (bci)", (FieldInstance fieldA, FieldInstance fieldB, MatchingEnv env) => {
	// 		String ownerA = fieldAf.containingType.getName();
	// 		String nameA = fieldA.getName();
	// 		String descA = fieldA.getDesc();
	// 		String ownerB = fieldBf.containingType.getName();
	// 		String nameB = fieldB.getName();
	// 		String descB = fieldB.getDesc();

	// 		int matched = 0;
	// 		int mismatched = 0;

	// 		foreach (MethodInstance src in fieldA.getReadRefs()) {
	// 			MethodInstance dst = src.getMatch();

	// 			if (dst == null || !fieldB.getReadRefs().contains(dst)) {
	// 				mismatched++;
	// 				continue;
	// 			}

	// 			int[] map = ClassifierUtil.mapInsns(src, dst);
	// 			if (map == null) continue;

	// 			InsnList ilA = src.getAsmNode().instructions;
	// 			InsnList ilB = dst.getAsmNode().instructions;

	// 			for (int srcIdx = 0; srcIdx < map.Length; srcIdx++) {
	// 				if (map[srcIdx] < 0) continue;

	// 				AbstractInsnNode in_ = ilA.get(srcIdx);
	// 				if (in_.getOpcode() != Opcodes.GETFIELD && in_.getOpcode() != Opcodes.GETSTATIC) continue;

	// 				FieldInsnNode fin = (FieldInsnNode) in_;
	// 				if (!isSameField(fin, ownerA, nameA, descA, fieldA)) continue;

	// 				in_ = ilB.get(map[srcIdx]);
	// 				fin = (FieldInsnNode) in_;

	// 				if (!isSameField(fin, ownerB, nameB, descB, fieldB)) {
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

	// private static AbstractClassifier writeRefsBci = new AbstractClassifier("write refs (bci)", (FieldInstance fieldA, FieldInstance fieldB, MatchingEnv env) => {
	// 		String ownerA = fieldAf.containingType.getName();
	// 		String nameA = fieldA.getName();
	// 		String descA = fieldA.getDesc();
	// 		String ownerB = fieldBf.containingType.getName();
	// 		String nameB = fieldB.getName();
	// 		String descB = fieldB.getDesc();

	// 		int matched = 0;
	// 		int mismatched = 0;

	// 		foreach (MethodInstance src in fieldA.getWriteRefs()) {
	// 			MethodInstance dst = src.getMatch();

	// 			if (dst == null || !fieldB.getWriteRefs().contains(dst)) {
	// 				mismatched++;
	// 				continue;
	// 			}

	// 			int[] map = ClassifierUtil.mapInsns(src, dst);
	// 			if (map == null) continue;

	// 			InsnList ilA = src.getAsmNode().instructions;
	// 			InsnList ilB = dst.getAsmNode().instructions;

	// 			for (int srcIdx = 0; srcIdx < map.Length; srcIdx++) {
	// 				if (map[srcIdx] < 0) continue;

	// 				AbstractInsnNode in_ = ilA.get(srcIdx);
	// 				if (in_.getOpcode() != Opcodes.PUTFIELD && in_.getOpcode() != Opcodes.PUTSTATIC) continue;

	// 				FieldInsnNode fin = (FieldInsnNode) in_;
	// 				if (!isSameField(fin, ownerA, nameA, descA, fieldA)) continue;

	// 				in_ = ilB.get(map[srcIdx]);
	// 				fin = (FieldInsnNode) in_;

	// 				if (!isSameField(fin, ownerB, nameB, descB, fieldB)) {
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

	// private static bool isSameField(FieldInsnNode fin, String owner, String name, String desc, FieldInstance field) {
	// 	ClassInstance target;

	// 	return fin.name.equals(name)
	// 			&& fin.desc.equals(desc)
	// 			&& (fin.owner.equals(owner) || (target = field.getEnv().getClsByName(fin.owner)) != null && target.resolveField(name, desc) == field);
	// }

	private static bool checkAsmNodes(FieldInstance a, FieldInstance b) {
		return a.cecilField != null && b.cecilField != null;
	}

	private static double compareAsmNodes(FieldInstance a, FieldInstance b) {
		return a.cecilField == null && b.cecilField == null ? 1 : 0;
	}

	public class AbstractClassifier : IClassifier<FieldInstance> {
		private readonly string name;
		public double weight; // probably shouldn't be public but I'm lazy and csharp nested types have different visibility rules so I can't just do private
		private Func<FieldInstance, FieldInstance, MatchingEnv, double> classifierFunc;

		public AbstractClassifier(string name, Func<FieldInstance, FieldInstance, MatchingEnv, double> classifierFunc) {
			this.name = name;
			this.classifierFunc = classifierFunc;
		}

		public String getName() {
			return name;
		}

		public double getWeight() {
			return weight;
		}

		public double getScore(FieldInstance a, FieldInstance b, MatchingEnv env) {
			return classifierFunc.Invoke(a, b, env);
		}
	}
}
