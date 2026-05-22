using System.Diagnostics.CodeAnalysis;

namespace Matcher.Matching;

public class LocalClassEnv {
	// initializing a non-nullable field as null bc it gets set immediately afterwards anyway
	private LocalClassEnv other = null!;
	public required MatchingEnv SharedEnv { get; init; }
	public Dictionary<string, TypeInstance> types = [];

	[SetsRequiredMembers]
	public LocalClassEnv(MatchingEnv sharedEnv) {
		this.SharedEnv = sharedEnv;
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

	public TypeInstance GetCreateTypeInstance(string id) {
		return GetCreateTypeInstance(id, true)!;
	}

	public TypeInstance? GetCreateTypeInstance(string id, bool createUnknown) {
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
	public void SetOther(LocalClassEnv other) {
		this.other = other;
	}
	public LocalClassEnv GetOther() {
		return other;
	}
}