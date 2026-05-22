namespace Matcher.Matching.Classifier;

public class FieldClassifier {
	public static void Init() {
		AddClassifier(fieldStaticCheck, 10);
		AddClassifier(accessFlags, 4);
		AddClassifier(type, 10);
		// addClassifier(signature, 5);
		AddClassifier(readReferences, 6);
		AddClassifier(writeReferences, 6);
		AddClassifier(position, 3);
		// addClassifier(initValue, 7);
		// addClassifier(initStrings, 8);
		// addClassifier(initCode, 10, ClassifierLevel.Intermediate, ClassifierLevel.Full, ClassifierLevel.Extra);
		// addClassifier(readRefsBci, 6, ClassifierLevel.Extra);
		// addClassifier(writeRefsBci, 6, ClassifierLevel.Extra);
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

	public static List<RankResult<FieldInstance>> Rank(FieldInstance src, FieldInstance[] dsts, ClassifierLevel level, MatchingEnv env) {
		return Rank(src, dsts, level, env, double.PositiveInfinity);
	}

	public static List<RankResult<FieldInstance>> Rank(FieldInstance src, FieldInstance[] dsts, ClassifierLevel level, MatchingEnv env, double maxMismatch) {
		return ClassifierUtil.Rank(src, dsts, classifiers.GetValueOrDefault(level, []), ClassifierUtil.CheckPotentialEquality, env, maxMismatch);
	}

	private static readonly Dictionary<ClassifierLevel, List<IClassifier<FieldInstance>>> classifiers = [];
	private static readonly Dictionary<ClassifierLevel, double> maxScore = [];

	private static readonly AbstractClassifier fieldStaticCheck = new("field static check", (fieldA, fieldB, env) => {
			if (!CheckAsmNodes(fieldA, fieldB)) return CompareAsmNodes(fieldA, fieldB);

			return fieldA.CecilField.IsStatic == fieldB.CecilField.IsStatic ? 1 : 0;
		}
	);

	private static readonly AbstractClassifier accessFlags = new("access flags", (fieldA, fieldB, env) => {
			if (!CheckAsmNodes(fieldA, fieldB)) return CompareAsmNodes(fieldA, fieldB);

			// int mask = (Opcodes.ACC_PUBLIC | Opcodes.ACC_PROTECTED | Opcodes.ACC_PRIVATE) | Opcodes.ACC_FINAL | Opcodes.ACC_VOLATILE | Opcodes.ACC_TRANSIENT | Opcodes.ACC_SYNTHETIC;
			// int resultA = fieldA.getAsmNode().access & mask;
			// int resultB = fieldB.getAsmNode().access & mask;

			// return 1 - Integer.bitCount(resultA ^ resultB) / 6;

			int diff = 0;

			bool hasSameAccess = (fieldA.CecilField.IsPublic == fieldB.CecilField.IsPublic)
				&& (fieldA.CecilField.IsFamilyOrAssembly == fieldB.CecilField.IsFamilyOrAssembly)
				&& (fieldA.CecilField.IsFamily == fieldB.CecilField.IsFamily)
				&& (fieldA.CecilField.IsFamilyAndAssembly == fieldB.CecilField.IsFamilyAndAssembly)
				&& (fieldA.CecilField.IsAssembly == fieldB.CecilField.IsAssembly)
				&& (fieldA.CecilField.IsPrivate == fieldB.CecilField.IsPrivate);

			if (!hasSameAccess) diff += 1;

			// TODO field flags other than access

			return 1 - diff;
		}
	);

	private static readonly AbstractClassifier type = new("types", (fieldA, fieldB, env) => {
			return ClassifierUtil.CheckPotentialEquality(fieldA.ContainingType, fieldB.ContainingType) ? 1 : 0;
		}
	);

	// private static readonly AbstractClassifier signature = new("signature", (fieldA, fieldB, env) => {
	// 		FieldSignature sigA = fieldA.getSignature();
	// 		FieldSignature sigB = fieldB.getSignature();

	// 		if (sigA == null && sigB == null) return 1;
	// 		if (sigA == null || sigB == null) return 0;

	// 		return sigA.isPotentiallyEqual(sigB) ? 1 : 0;
	// 	}
	// );

	private static readonly AbstractClassifier readReferences = new("read references", (fieldA, fieldB, env) => {
			return ClassifierUtil.CompareMethodSets(fieldA.readRefs, fieldB.readRefs, true);
		}
	);

	private static readonly AbstractClassifier writeReferences = new("write references", (fieldA, fieldB, env) => {
			return ClassifierUtil.CompareMethodSets(fieldA.writeRefs, fieldB.writeRefs, true);
		}
	);

	private static readonly AbstractClassifier position = new("position", (fieldA, fieldB, env) => {
			/*if (fieldA.position == fieldB.position) return 1;

			double relPosA = ClassifierUtil.getRelativePosition(fieldA.position, fieldA.cls.fields.size());
			double relPosB = ClassifierUtil.getRelativePosition(fieldB.position, fieldB.cls.fields.size());

			return 1 - Math.abs(relPosA - relPosB);*/
			return ClassifierUtil.ClassifyPosition(fieldA, fieldB, field => field.Position, (f, idx) => f.ContainingType.fieldsOrdered[idx], f => f.ContainingType.fieldsOrdered);
		}
	);

	// private static readonly AbstractClassifier initValue = new("init value", (fieldA, fieldB, env) => {
	// 		if (!checkAsmNodes(fieldA, fieldB)) return compareAsmNodes(fieldA, fieldB);

	// 		Object valA = fieldA.getAsmNode().value;
	// 		Object valB = fieldB.getAsmNode().value;

	// 		if (valA == null && valB == null) return 1;
	// 		if (valA == null || valB == null) return 0;

	// 		return valA.equals(valB) ? 1 : 0;
	// 	}
	// );

	// private static readonly AbstractClassifier initStrings = new("init strings", (fieldA, fieldB, env) => {
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

	// private static readonly AbstractClassifier initCode = new("init code", (fieldA, fieldB, env) => {
	// 		List<AbstractInsnNode> initA = fieldA.getInitializer();
	// 		List<AbstractInsnNode> initB = fieldB.getInitializer();

	// 		if (initA == null && initB == null) return 1;
	// 		if (initA == null || initB == null) return 0;

	// 		return ClassifierUtil.compareInsns(initA, initB, env);
	// 	}
	// );

	// private static readonly AbstractClassifier readRefsBci = new("read refs (bci)", (fieldA, fieldB, env) => {
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

	// private static readonly AbstractClassifier writeRefsBci = new("write refs (bci)", (fieldA, fieldB, env) => {
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

	private static bool CheckAsmNodes(FieldInstance a, FieldInstance b) {
		return a.CecilField != null && b.CecilField != null;
	}

	private static double CompareAsmNodes(FieldInstance a, FieldInstance b) {
		return a.CecilField == null && b.CecilField == null ? 1 : 0;
	}

	public class AbstractClassifier(string name, Func<FieldInstance, FieldInstance, MatchingEnv, double> classifierFunc) : IClassifier<FieldInstance> {
		private readonly string name = name;
		public double weight; // probably shouldn't be public but I'm lazy and csharp nested types have different visibility rules so I can't just do private
		private readonly Func<FieldInstance, FieldInstance, MatchingEnv, double> classifierFunc = classifierFunc;

		public string GetName() {
			return name;
		}

		public double GetWeight() {
			return weight;
		}

		public double GetScore(FieldInstance a, FieldInstance b, MatchingEnv env) {
			return classifierFunc.Invoke(a, b, env);
		}
	}
}
