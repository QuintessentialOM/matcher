using System.Linq;
using System.Text.RegularExpressions;
using Matcher.Matching;
using Matcher.Matching.Classifier;
using Mono.Cecil;

namespace Matcher;

public class Matcher {
	public static readonly Regex NonObfuscatedPattern = new Regex("^[a-zA-Z_\\`][a-zA-Z0-9_\\`]*(\\[])*$");

	public MatchingEnv env;
	LocalClassEnv envA;
	LocalClassEnv envB;

	public Matcher() {
		env = new();
		envA = env.envA;
		envB = env.envB;
	}


	static Collection<TypeDefinition> CollectNestedTypes(Collection<TypeDefinition> topLevel) {
		var types = new Collection<TypeDefinition>();
		foreach(var type in topLevel)
			VisitTypes(type, types.Add);
		return types;
	}
	
	static void VisitTypes(TypeDefinition top, Action<TypeDefinition> act) {
		act(top);
		foreach(var type in top.NestedTypes)
			VisitTypes(type, act);
	}

	public void Init(ModuleDefinition moduleA, ModuleDefinition moduleB) {
		// TODO want to preprocess by replacing string deobf method calls with `ldstr`, like OpusMutatum does

		// TODO env init stuff - see ClassFeatureExtractor.process
		// what fabric matcher does:
		// step A: methods/fields, outer classes, super classes and interfaces, and collect strings that appear in classes
		// step B: method bodies: field accesses, method invocations, class instantiation etc
		// step C: construct method hierarchies based on class hierarchies
		// step D: determine method parent/child relations and do some field stuff idk
		// step E: assign temporary names idk

		foreach (TypeDefinition type in CollectNestedTypes(moduleA.Types)) {
			envA.types[type.Name] = new TypeInstance(envA, type, !NonObfuscatedPattern.IsMatch(type.Name));
		}
		foreach (TypeDefinition type in CollectNestedTypes(moduleB.Types)) {
			envB.types[type.Name] = new TypeInstance(envB, type, !NonObfuscatedPattern.IsMatch(type.Name));
		}
		// ToList to copy since we mutate the types dictionary during initialization (to add extra types not present in the ModuleDefinition itself)
		foreach (var type in envA.types.Values.ToList()) {
			InitTypeA(type, envA);
		}
		foreach (var type in envB.types.Values.ToList()) {
			InitTypeA(type, envB);
		}
		MatchUnobfuscated();
	}

	private void InitTypeA(TypeInstance cls, LocalClassEnv env) {
		foreach (var method in cls.cecilType.Methods) {
			var methodInstance = new MethodInstance(env, cls, method, !NonObfuscatedPattern.IsMatch(method.Name));
			cls.methodsById[methodInstance.getId()] = methodInstance;
		}
		foreach (var field in cls.cecilType.Fields) {
			var fieldInstance = new FieldInstance(env, cls, field, !NonObfuscatedPattern.IsMatch(field.Name));
			cls.fieldsById[fieldInstance.getId()] = fieldInstance;
		}
		foreach (var genericParam in cls.cecilType.GenericParameters) {
			var genericParamInstance = new GenericParamInstance(env, cls, genericParam, !NonObfuscatedPattern.IsMatch(genericParam.Name));
			cls.genericParamsById[genericParamInstance.getId()] = genericParamInstance;
		}

		// TODO collect strings
		var parent = cls.cecilType.BaseType;
		if (parent != null) {
			var parentTypeInstance = env.getCreateTypeInstance(parent.Name);
			parentTypeInstance.childTypes.Add(cls);
			cls.baseType = parentTypeInstance;
		}
		foreach (var nestedType in cls.cecilType.NestedTypes) {
			var nestedTypeInstance = env.getCreateTypeInstance(nestedType.Name);
			nestedTypeInstance.outerType = cls;
			cls.nestedTypes.Add(nestedTypeInstance);
		}
		foreach (var iface in cls.cecilType.Interfaces) {
			var ifaceInstance = env.getCreateTypeInstance(iface.InterfaceType.Name);
			ifaceInstance.implementedBy.Add(cls);
			cls.interfaces.Add(ifaceInstance);
		}
	}

	private void MatchUnobfuscated() {
		foreach (var typeName in envA.types.Keys) {
			var type = envA.types[typeName];
			if (type.isNameObfuscated) continue;
			var match = envB.types!.GetValueOrDefault(typeName, null);
			if (match != null && !match.isNameObfuscated) {
				MatchType(type, match);
			}
		}
	}

	public void MatchType(TypeInstance a, TypeInstance b) {
		if (a == null) throw new NullReferenceException("null class A");
		if (b == null) throw new NullReferenceException("null class B");
		if (a.getArrayDimensions() != b.getArrayDimensions()) throw new ArgumentException("the classes don't have the same amount of array dimensions");
		if (a.getMatch() == b) return;

		// TODO logger
		// LOGGER.debug("Matching class {} => {}{}", a, b, (a.hasMappedName() ? " ("+a.getName(NameType.MAPPED_PLAIN)+")" : ""));

		if (a.getMatch() != null) {
			a.getMatch()!.setMatch(null);
			UnmatchMembersAndGenerics(a);
		}

		if (b.getMatch() != null) {
			b.getMatch()!.setMatch(null);
			UnmatchMembersAndGenerics(b);
		}

		a.setMatch(b);
		b.setMatch(a);

		// match all array dimensionalities for the corresponding type
		if (a.isArray()) {
			var elemA = a.elementType;
			if (!elemA!.hasMatch()) MatchType(elemA, b.elementType!);
		} else {
			foreach (var arrayA in a.arrays) {
				var dims = arrayA.getArrayDimensions();

				foreach (var arrayB in b.arrays) {
					if (arrayB.hasMatch() || arrayB.getArrayDimensions() != dims) continue;
					MatchType(arrayA, arrayB);
					break;
				}
			}
		}

		foreach (MethodInstance src in a.methodsById.Values) {
			if (!src.isNameObfuscated) {
				MethodInstance? dst = b.methodsById!.GetValueOrDefault(src.getId(), null);

				if ((dst != null || (dst = b.getMethod(src.getName(), null)) != null) && !dst.isNameObfuscated) { // full match or name match with no alternatives
					MatchMethod(src, dst!);
					continue;
				}
			}

			MethodHierarchyData? matchedDst = src.hierarchyData?.matchedHierarchy;
			if (matchedDst == null) continue;

			ISet<MethodInstance> dstHierarchyMembers = matchedDst!.members;
			if (dstHierarchyMembers.Count <= 1) continue;

			foreach (MethodInstance dst in b.methodsById.Values) {
				if (dstHierarchyMembers.Contains(dst)) {
					src.setMatchable(true);
					dst.setMatchable(true);
					MatchMethod(src, dst);
					break;
				}
			}
		}

		// match fields that are not obfuscated

		foreach (FieldInstance src in a.fieldsById.Values) {
			if (!src.isNameObfuscated) {
				FieldInstance? dst = b.fieldsById!.GetValueOrDefault(src.getId(), null);

				if ((dst != null || (dst = b.getField(src.getName(), null)) != null) && !dst.isNameObfuscated) { // full match or name match with no alternatives
					MatchField(src, dst);
				}
			}
		}

		// TODO generics
	}
	
	public void MatchMethod(MethodInstance a, MethodInstance b) {
		if (a == null) throw new NullReferenceException("null method A");
		if (b == null) throw new NullReferenceException("null method B");
		// if (a.getCls().getMatch() != b.getCls()) throw new IllegalArgumentException("the methods don't belong to the same class");
		if (a.getMatch() == b) return;

		// LOGGER.debug("Matching method {} => {}{}", a, b, (a.hasMappedName() ? " ("+a.getName(NameType.MAPPED_PLAIN)+")" : ""));

		ISet<MethodInstance>? membersA = a.hierarchyData?.members;
		ISet<MethodInstance>? membersB = b.hierarchyData?.members;
		// assert membersA.contains(a);
		// assert membersB.contains(b);

		if (a.hierarchyData != null && a.hierarchyData.matchedHierarchy != b.hierarchyData) {
			if (a.hierarchyData?.matchedHierarchy != null) {
				foreach (MethodInstance m in membersA!) {
					if (m.hasMatch()) {
						UnmatchMethodParams(m);
						m.getMatch()!.setMatch(null);
						m.setMatch(null);
					}
				}
			}

			if (b.hierarchyData?.matchedHierarchy != null) {
				foreach (MethodInstance m in membersB!) {
					if (m.hasMatch()) {
						UnmatchMethodParams(m);
						m.getMatch()!.setMatch(null);
						m.setMatch(null);
					}
				}
			}

			// LocalClassEnv reqEnv = a.getCls().getEnv();

			if (membersA != null && membersB != null) {
				foreach (MethodInstance ca in membersA) {
					TypeInstance cls = ca.containingType;
					if (!cls.hasMatch()/* || cls.getEnv() != reqEnv*/) continue;

					foreach (MethodInstance cb in cls.getMatch()!.methodsById.Values) {
						if (membersB.Contains(cb)) {
							// assert !ca.hasMatch() && !cb.hasMatch();
							ca.setMatch(cb);
							cb.setMatch(ca);
							break;
						}
					}
				}
			}
		} else {
			if (a.getMatch() != null) {
				UnmatchMethodParams(a);
				a.getMatch()!.setMatch(null);
				a.setMatch(null);
			}

			if (b.getMatch() != null) {
				UnmatchMethodParams(b);
				b.getMatch()!.setMatch(null);
				b.setMatch(null);
			}

			a.setMatch(b);
			b.setMatch(a);
		}
	}

	public void MatchGenericParam(GenericParamInstance a, GenericParamInstance b) {
		if (a == null) throw new NullReferenceException("null generic param A");
		if (b == null) throw new NullReferenceException("null generic param B");
		// if (a.getCls().getMatch() != b.getCls()) throw new IllegalArgumentException("the methods don't belong to the same class");
		if (a.getMatch() == b) return;

		// LOGGER.debug("Matching field {} => {}{}", a, b, (a.hasMappedName() ? " ("+a.getName(NameType.MAPPED_PLAIN)+")" : ""));

		if (a.getMatch() != null) a.getMatch()!.setMatch(null);
		if (b.getMatch() != null) b.getMatch()!.setMatch(null);

		a.setMatch(b);
		b.setMatch(a);
	}

	public void MatchField(FieldInstance a, FieldInstance b) {
		if (a == null) throw new NullReferenceException("null field A");
		if (b == null) throw new NullReferenceException("null field B");
		// if (a.getCls().getMatch() != b.getCls()) throw new IllegalArgumentException("the methods don't belong to the same class");
		if (a.getMatch() == b) return;

		// LOGGER.debug("Matching field {} => {}{}", a, b, (a.hasMappedName() ? " ("+a.getName(NameType.MAPPED_PLAIN)+")" : ""));

		if (a.getMatch() != null) a.getMatch()!.setMatch(null);
		if (b.getMatch() != null) b.getMatch()!.setMatch(null);

		a.setMatch(b);
		b.setMatch(a);
	}

	public void MatchMethodParam(MethodParamInstance a, MethodParamInstance b) {
		if (a == null) throw new NullReferenceException("null method var A");
		if (b == null) throw new NullReferenceException("null method var B");
		// if (a.getMethod().getMatch() != b.getMethod()) throw new IllegalArgumentException("the method vars don't belong to the same method");
		// if (a.isArg() != b.isArg()) throw new IllegalArgumentException("the method vars are not of the same kind");
		if (a.getMatch() == b) return;

		// LOGGER.debug("Matching method arg {} => {}{}", a, b, (a.hasMappedName() ? " ("+a.getName(NameType.MAPPED_PLAIN)+")" : ""));

		if (a.getMatch() != null) a.getMatch()!.setMatch(null);
		if (b.getMatch() != null) b.getMatch()!.setMatch(null);

		a.setMatch(b);
		b.setMatch(a);
	}

	public void UnmatchType(TypeInstance cls) {
		if (cls == null) throw new NullReferenceException("null class");
		if (cls.getMatch() == null) return;

		// LOGGER.debug("Unmatching class {} (was {}){}", cls, cls.getMatch(), (cls.hasMappedName() ? " ("+cls.getName(NameType.MAPPED_PLAIN)+")" : ""));

		cls.getMatch()!.setMatch(null);
		cls.setMatch(null);

		UnmatchMembersAndGenerics(cls);

		if (cls.isArray()) {
			UnmatchType(cls.elementType!);
		} else {
			foreach (TypeInstance array in cls.arrays) {
				UnmatchType(array);
			}
		}
	}

	private static void UnmatchMembersAndGenerics(TypeInstance cls) {
		foreach (MethodInstance m in cls.methodsById.Values) {
			if (m.getMatch() != null) {
				m.getMatch()!.setMatch(null);
				m.setMatch(null);

				UnmatchMethodParams(m);
			}
		}

		foreach (FieldInstance m in cls.fieldsById.Values) {
			if (m.getMatch() != null) {
				m.getMatch()!.setMatch(null);
				m.setMatch(null);
			}
		}

		foreach (GenericParamInstance m in cls.genericParamsById.Values) {
			if (m.getMatch() != null) {
				m.getMatch()!.setMatch(null);
				m.setMatch(null);
			}
		}
	}

	public void UnmatchMethod(MethodInstance m) {
		if (m == null) throw new NullReferenceException("null member");
		if (m.getMatch() == null) return;

		// LOGGER.debug("Unmatching member {} (was {}){}", m, m.getMatch(), (m.hasMappedName() ? " ("+m.getName(NameType.MAPPED_PLAIN)+")" : ""));

		UnmatchMethodParams(m);

		m.getMatch()!.setMatch(null);
		m.setMatch(null);

		if (m.hierarchyData != null) {
			foreach (MethodInstance member in m.hierarchyData.members) {
				UnmatchMethod(member);
			}
		}
	}

	public void UnmatchField(FieldInstance f) {
		if (f == null) throw new NullReferenceException("null member");
		if (f.getMatch() == null) return;

		// LOGGER.debug("Unmatching member {} (was {}){}", f, f.getMatch(), (f.hasMappedName() ? " ("+f.getName(NameType.MAPPED_PLAIN)+")" : ""));

		f.getMatch()!.setMatch(null);
		f.setMatch(null);
	}

	public void UnmatchGenericParam(GenericParamInstance f) {
		if (f == null) throw new NullReferenceException("null member");
		if (f.getMatch() == null) return;

		// LOGGER.debug("Unmatching member {} (was {}){}", f, f.getMatch(), (f.hasMappedName() ? " ("+f.getName(NameType.MAPPED_PLAIN)+")" : ""));

		f.getMatch()!.setMatch(null);
		f.setMatch(null);
	}

	public void UnmatchMethodParam(MethodParamInstance a) {
		if (a == null) throw new NullReferenceException("null method param");
		if (a.getMatch() == null) return;

		// LOGGER.debug("Unmatching method var {} (was {}){}", a, a.getMatch(), (a.hasMappedName() ? " ("+a.getName(NameType.MAPPED_PLAIN)+")" : ""));

		a.getMatch()!.setMatch(null);
		a.setMatch(null);
	}

	private static void UnmatchMethodParams(MethodInstance m) {
		foreach (MethodParamInstance arg in m.args) {
			if (arg.getMatch() != null) {
				arg.getMatch()!.setMatch(null);
				arg.setMatch(null);
			}
		}
	}








	// auto matching process:
	// classes at Initial, once or twice
	// loop methods/fields/classes at Intermediate until no new matches
	// loop methods/fields/classes at Full until no new matches
	// loop methods/fields/classes at Extra until no new matches
	// loop methods params/vars at Full until no new matches
	private static readonly ClassifierLevel autoMatchMaxLevel = ClassifierLevel.Extra;


	private const double absClassAutoMatchThreshold = 0.85;
	private const double relClassAutoMatchThreshold = 0.085;
	private const double absMethodAutoMatchThreshold = 0.85;
	private const double relMethodAutoMatchThreshold = 0.085;
	private const double absFieldAutoMatchThreshold = 0.85;
	private const double relFieldAutoMatchThreshold = 0.085;
	private const double absMethodArgAutoMatchThreshold = 0.85;
	private const double relMethodArgAutoMatchThreshold = 0.085;
	private const double absMethodVarAutoMatchThreshold = 0.85;
	private const double relMethodVarAutoMatchThreshold = 0.085;
	public const bool assumeBothOrNoneObfuscated = false;


	public void autoMatchAll(Action<double> progressReceiver) {
		if (autoMatchClasses(ClassifierLevel.Initial, absClassAutoMatchThreshold, relClassAutoMatchThreshold, progressReceiver)) {
			autoMatchClasses(ClassifierLevel.Initial, absClassAutoMatchThreshold, relClassAutoMatchThreshold, progressReceiver);
		}

		autoMatchLevel(ClassifierLevel.Intermediate, progressReceiver);
		autoMatchLevel(ClassifierLevel.Full, progressReceiver);
		autoMatchLevel(ClassifierLevel.Extra, progressReceiver);

		bool matchedAny;

		do {
			matchedAny = autoMatchMethodArgs(ClassifierLevel.Full, absMethodArgAutoMatchThreshold, relMethodArgAutoMatchThreshold, progressReceiver);
			// matchedAny |= autoMatchMethodVars(ClassifierLevel.Full, absMethodVarAutoMatchThreshold, relMethodVarAutoMatchThreshold, progressReceiver);
		} while (matchedAny);
	}

	private void autoMatchLevel(ClassifierLevel level, Action<double> progressReceiver) {
		bool matchedAny;
		bool matchedClassesBefore = true;

		do {
			matchedAny = autoMatchMethods(level, absMethodAutoMatchThreshold, relMethodAutoMatchThreshold, progressReceiver);
			matchedAny |= autoMatchFields(level, absFieldAutoMatchThreshold, relFieldAutoMatchThreshold, progressReceiver);

			if (!matchedAny && !matchedClassesBefore) {
				break;
			}

			matchedAny |= matchedClassesBefore = autoMatchClasses(level, absClassAutoMatchThreshold, relClassAutoMatchThreshold, progressReceiver);
		} while (matchedAny);
	}

	public bool autoMatchClasses(Action<double> progressReceiver) {
		return autoMatchClasses(autoMatchMaxLevel, absClassAutoMatchThreshold, relClassAutoMatchThreshold, progressReceiver);
	}

	public bool autoMatchClasses(ClassifierLevel level, double absThreshold, double relThreshold, Action<double> progressReceiver) {
		Func<TypeInstance, bool> filter = cls => cls.isReal() && (!assumeBothOrNoneObfuscated || cls.isNameObfuscated) && !cls.hasMatch() && cls.isMatchable();

		List<TypeInstance> classes = new List<TypeInstance>(envA.types.Values).Where(filter).ToList();

		// TypeInstance[] cmpClasses = new List<TypeInstance>(envB.types.Values).Where(filter).ToList();
		List<TypeInstance> cmpClasses = new List<TypeInstance>(envB.types.Values).Where(filter).ToList();

		double maxScore = TypeClassifier.getMaxScore(level);
		double maxMismatch = maxScore - ClassifierUtil.getRawScore(absThreshold * (1 - relThreshold), maxScore);
		Dictionary<TypeInstance, TypeInstance> matches = [];//new ConcurrentHashDictionary<>(classes.Count);

		// runInParallel(classes, cls => {
		// 	List<RankResult<TypeInstance>> ranking = TypeClassifier.rank(cls, cmpClasses, level, env, maxMismatch);

		// 	if (ClassifierUtil.checkRank(ranking, absThreshold, relThreshold, maxScore)) {
		// 		TypeInstance match = ranking.get(0).getSubject();

		// 		matches.put(cls, match);
		// 	}
		// }, progressReceiver);

		foreach (var cls in classes) {
			List<RankResult<TypeInstance>> ranking = TypeClassifier.rank(cls, cmpClasses.ToArray(), level, env, maxMismatch);

			if (ClassifierUtil.checkRank(ranking, absThreshold, relThreshold, maxScore)) {
				TypeInstance match = ranking[0].subject;

				matches[cls] = match;

				Console.WriteLine($"{cls.getName()} -> {match.getName()}");
			}
		}

		sanitizeMatches(matches);

		foreach (var entry in matches) {
			MatchType(entry.Key, entry.Value);
		}

		// LOGGER.info("Auto matched {} classes ({} unmatched, {} total)", matches.Count, (classes.Count - matches.Count), envA.types.Count);

		return matches.Count != 0;
	}

	// public static void runInParallel<T, C>(List<T> workSet, Consumer<T> worker, Action<double> progressReceiver) {
	// 	if (workSet.Count == 0) return;

	// 	int itemsDone = 0; // originally AtomicInteger
	// 	int updateRate = Math.max(1, workSet.Count / 200);

	// 	try {
	// 		List<Future<Void>> futures = threadPool.invokeAll(workSet.stream().<Callable<Void>>map(workItem => () => {
	// 			worker.accept(workItem);

	// 			int cItemsDone = itemsDone.incrementAndGet();

	// 			if ((cItemsDone % updateRate) == 0) {
	// 				progressReceiver.accept((double) cItemsDone / workSet.Count);
	// 			}

	// 			return null;
	// 		}).collect(Collectors.toList()));

	// 		for (Future<Void> future : futures) {
	// 			future.get();
	// 		}
	// 	} catch (ExecutionException | InterruptedException e) {
	// 		throw new RuntimeException(e);
	// 	}
	// }

	public bool autoMatchMethods(Action<double> progressReceiver) {
		return autoMatchMethods(autoMatchMaxLevel, absMethodAutoMatchThreshold, relMethodAutoMatchThreshold, progressReceiver);
	}

	public bool autoMatchMethods(ClassifierLevel level, double absThreshold, double relThreshold, Action<double> progressReceiver) {
		int totalUnmatched = 0; // originally AtomicInteger
		Dictionary<MethodInstance, MethodInstance> matches = matchMembers(level, absThreshold, relThreshold,
				cls => cls.methodsById.Values.ToArray(), MethodClassifier.rank, MethodClassifier.getMaxScore(level),
				progressReceiver, ref totalUnmatched);

		foreach (var entry in matches) {
			MatchMethod(entry.Key, entry.Value);
		}

		// LOGGER.info("Auto matched {} methods ({} unmatched)", matches.Count, totalUnmatched);

		return matches.Count != 0;
	}

	public bool autoMatchFields(Action<double> progressReceiver) {
		return autoMatchFields(autoMatchMaxLevel, absFieldAutoMatchThreshold, relFieldAutoMatchThreshold, progressReceiver);
	}

	public bool autoMatchFields(ClassifierLevel level, double absThreshold, double relThreshold, Action<double> progressReceiver) {
		int totalUnmatched = 0; // originally AtomicInteger
		double maxScore = FieldClassifier.getMaxScore(level);

		Dictionary<FieldInstance, FieldInstance> matches = matchMembers(level, absThreshold, relThreshold,
				cls => cls.fieldsById.Values.ToArray(), FieldClassifier.rank, maxScore,
				progressReceiver, ref totalUnmatched);

		foreach (var entry in matches) {
			MatchField(entry.Key, entry.Value);
		}

		// LOGGER.info("Auto matched {} fields ({} unmatched)", matches.Count, totalUnmatched);

		return matches.Count != 0;
	}

	delegate List<RankResult<T>> IRanker<T>(T src, T[] dsts, ClassifierLevel level, MatchingEnv env, double maxMismatch);

	// <T extends MemberInstance<T>>
	private Dictionary<T, T> matchMembers<T>(ClassifierLevel level, double absThreshold, double relThreshold,
			Func<TypeInstance, T[]> memberGetter, IRanker<T> ranker, double maxScore,
			Action<double> progressReceiver, ref int totalUnmatched) where T : MatchableMember {
		List<TypeInstance> classes = env.envA.types.Values
				.Where(cls => /*cls.isReal() &&*/ cls.hasMatch() && memberGetter.Invoke(cls).Length > 0)
				.Where(cls => {
					foreach (T member in memberGetter.Invoke(cls)) {
						if (!member.hasMatch() && member.isMatchable()) return true;
					}

					return false;
				})
				.ToList();
		if (classes.Count == 0) return [];

		double maxMismatch = maxScore - ClassifierUtil.getRawScore(absThreshold * (1 - relThreshold), maxScore);
		Dictionary<T, T> ret = new();//new ConcurrentHashDictionary<>(512);

		// runInParallel(classes, cls => {
		// 	int unmatched = 0;

		// 	foreach (T member in memberGetter.apply(cls)) {
		// 		if (member.hasMatch() || !member.isMatchable()) continue;

		// 		List<RankResult<T>> ranking = ranker.rank(member, memberGetter.apply(cls.getMatch()), level, env, maxMismatch);

		// 		if (ClassifierUtil.checkRank(ranking, absThreshold, relThreshold, maxScore)) {
		// 			T match = ranking.get(0).getSubject();

		// 			ret.put(member, match);
		// 		} else {
		// 			unmatched++;
		// 		}
		// 	}

		// 	if (unmatched > 0) totalUnmatched.addAndGet(unmatched);
		// }, progressReceiver);

		foreach (var cls in classes) {
			int unmatched = 0;

			foreach (T member in memberGetter.Invoke(cls)) {
				if (member.hasMatch() || !member.isMatchable()) continue;

				List<RankResult<T>> ranking = ranker.Invoke(member, memberGetter.Invoke(cls.getMatch()), level, env, maxMismatch);

				if (ClassifierUtil.checkRank(ranking, absThreshold, relThreshold, maxScore)) {
					T match = ranking[0].subject;

					ret[member] = match;
				} else {
					unmatched++;
				}
			}

			// if we parallelize again
			// if (unmatched > 0) Interlocked.Add(ref totalUnmatched, unmatched);
			if (unmatched > 0) totalUnmatched += unmatched;
		}

		sanitizeMatches(ret);

		return ret;
	}

	public bool autoMatchMethodArgs(Action<double> progressReceiver) {
		return autoMatchMethodArgs(autoMatchMaxLevel, absMethodArgAutoMatchThreshold, relMethodArgAutoMatchThreshold, progressReceiver);
	}

	public bool autoMatchMethodArgs(ClassifierLevel level, double absThreshold, double relThreshold, Action<double> progressReceiver) {
		return autoMatchMethodVars(true, methodInstance => methodInstance.args, level, absThreshold, relThreshold, progressReceiver);
	}

	// public bool autoMatchMethodVars(Action<double> progressReceiver) {
	// 	return autoMatchMethodVars(autoMatchMaxLevel, absMethodVarAutoMatchThreshold, relMethodVarAutoMatchThreshold, progressReceiver);
	// }

	// public bool autoMatchMethodVars(ClassifierLevel level, double absThreshold, double relThreshold, Action<double> progressReceiver) {
	// 	return autoMatchMethodVars(false, MethodInstance.getVars, level, absThreshold, relThreshold, progressReceiver);
	// }

	private bool autoMatchMethodVars(bool isArg, Func<MethodInstance, MethodParamInstance[]> supplier,
			ClassifierLevel level, double absThreshold, double relThreshold, Action<double> progressReceiver) {
		List<MethodInstance> methods = env.envA.types.Values
				.Where(cls => /*cls.isReal() &&*/ cls.hasMatch() && cls.methodsById.Count > 0)
				.SelectMany(cls => cls.methodsById.Values)
				.Where(m => m.hasMatch() && supplier.Invoke(m).Length > 0)
				.Where(m => {
					foreach (MethodParamInstance a in supplier.Invoke(m)) {
						if (!a.hasMatch() && a.isMatchable()) return true;
					}

					return false;
				})
				.ToList();
		Dictionary<MethodParamInstance, MethodParamInstance> matches;
		int totalUnmatched = 0; // originally AtomicInteger

		if (methods.Count == 0) {
			matches = [];
		} else {
			double maxScore = MethodParamClassifier.getMaxScore(level);
			double maxMismatch = maxScore - ClassifierUtil.getRawScore(absThreshold * (1 - relThreshold), maxScore);
			matches = new();//new ConcurrentHashDictionary<>(512);

			// runInParallel(methods, m => {
			// 	int unmatched = 0;

			// 	foreach (MethodVarInstance var in supplier.apply(m)) {
			// 		if (var.hasMatch() || !var.isMatchable()) continue;

			// 		List<RankResult<MethodVarInstance>> ranking = MethodVarClassifier.rank(var, supplier.apply(m.getMatch()), level, env, maxMismatch);

			// 		if (ClassifierUtil.checkRank(ranking, absThreshold, relThreshold, maxScore)) {
			// 			MethodVarInstance match = ranking.get(0).getSubject();

			// 			matches.put(var, match);
			// 		} else {
			// 			unmatched++;
			// 		}
			// 	}

			// 	if (unmatched > 0) totalUnmatched.addAndGet(unmatched);
			// }, progressReceiver);

			foreach (var m in methods) {
				int unmatched = 0;

				foreach (MethodParamInstance var in supplier.Invoke(m)) {
					if (var.hasMatch() || !var.isMatchable()) continue;

					List<RankResult<MethodParamInstance>> ranking = MethodParamClassifier.rank(var, supplier.Invoke(m.getMatch()), level, env, maxMismatch);

					if (ClassifierUtil.checkRank(ranking, absThreshold, relThreshold, maxScore)) {
						MethodParamInstance match = ranking[0].subject;

						matches[var] = match;
					} else {
						unmatched++;
					}
				}

				// if we parallelize again
				// if (unmatched > 0) Interlocked.Add(ref totalUnmatched, unmatched);
				if (unmatched > 0) totalUnmatched += unmatched;
			}

			sanitizeMatches(matches);
		}

		foreach (var entry in matches) {
			MatchMethodParam(entry.Key, entry.Value);
		}

		// LOGGER.info("Auto matched {} method {}s ({} unmatched)", matches.Count, (isArg ? "arg" : "var"), totalUnmatched);

		return matches.Count != 0;
	}

	public static void sanitizeMatches<T>(Dictionary<T, T> matches) where T : Matchable {
		HashSet<T> matched = new(new IdentityEqualityComparer<T>());
		HashSet<T> conflictingMatches = new(new IdentityEqualityComparer<T>());

		foreach (T cls in matches.Values) {
			if (!matched.Add(cls)) {
				conflictingMatches.Add(cls);
			}
		}

		if (conflictingMatches.Count != 0) {
			foreach (var entry in matches.Where(entry => conflictingMatches.Contains(entry.Value)).ToList()) {
				matches.Remove(entry.Key);
			}
		}
	}

	public record MatchingStatus(int totalClassCount, int matchedClassCount, int totalMethodCount, int matchedMethodCount,
			int totalMethodArgCount, int matchedMethodArgCount, int totalFieldCount, int matchedFieldCount) {}

	public MatchingStatus getStatus(bool inputsOnly) {
		int totalClassCount = 0;
		int matchedClassCount = 0;
		int totalMethodCount = 0;
		int matchedMethodCount = 0;
		int totalMethodArgCount = 0;
		int matchedMethodArgCount = 0;
		// int totalMethodVarCount = 0;
		// int matchedMethodVarCount = 0;
		int totalFieldCount = 0;
		int matchedFieldCount = 0;

		foreach (TypeInstance cls in env.envA.types.Values) {
			// if (inputsOnly && !cls.isInput()) continue;

			totalClassCount++;
			if (cls.hasMatch()) matchedClassCount++;

			foreach (MethodInstance method in cls.methodsById.Values) {
				// if (method.isReal()) {
					totalMethodCount++;

					if (method.hasMatch()) matchedMethodCount++;

					foreach (MethodParamInstance arg in method.args) {
						totalMethodArgCount++;

						if (arg.hasMatch()) matchedMethodArgCount++;
					}

					// foreach (MethodVarInstance var in method.getVars()) {
					// 	totalMethodVarCount++;

					// 	if (var.hasMatch()) matchedMethodVarCount++;
					// }
				// }
			}

			foreach (FieldInstance field in cls.fieldsById.Values) {
				// if (field.isReal()) {
					totalFieldCount++;

					if (field.hasMatch()) matchedFieldCount++;
				// }
			}
		}

		return new MatchingStatus(totalClassCount, matchedClassCount,
				totalMethodCount, matchedMethodCount,
				totalMethodArgCount, matchedMethodArgCount,
				// totalMethodVarCount, matchedMethodVarCount,
				totalFieldCount, matchedFieldCount);
	}
}
