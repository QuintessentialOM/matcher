using System.Diagnostics.CodeAnalysis;

namespace Matcher.Matching;

public class LocalClassEnv {
	// initializing a non-nullable field as null bc it gets set immediately afterwards anyway
	private LocalClassEnv other = null!;
	public required MatchingEnv sharedEnv { get; init; }
	public Dictionary<string, TypeInstance> types = new();

	[SetsRequiredMembers]
	public LocalClassEnv(MatchingEnv sharedEnv) {
		this.sharedEnv = sharedEnv;
	}

	// ICollection<TypeInstance> getTypes() {
	// 	return types.Values;
	// }

	// TypeInstance getClsByName(String name) {
	// 	return getClsById(TypeInstance.getId(name));
	// }

	// TypeInstance getClsById(String id);

	// TypeInstance getLocalClsByName(String name) {
	// 	return getLocalClsById(TypeInstance.getId(name));
	// }

	// TypeInstance getLocalClsById(String id);

	public TypeInstance getCreateTypeInstance(string id) {
		return getCreateTypeInstance(id, true)!;
	}

	public TypeInstance? getCreateTypeInstance(string id, bool createUnknown) {
		if (types.ContainsKey(id)) return types[id];
		if (!createUnknown) return null;
		types[id] = new TypeInstance(this, id, !Matcher.NonObfuscatedPattern.IsMatch(id));
		return types[id];
	}

	// TypeInstance getClsByName(String name, NameType nameType) {
	// 	return getClsById(TypeInstance.getId(name), nameType);
	// }

	// TypeInstance getClsById(String id, NameType nameType);

	// ClassEnvironment getGlobal();
	public void setOther(LocalClassEnv other) {
		this.other = other;
	}
	public LocalClassEnv getOther() {
		return other;
	}
}