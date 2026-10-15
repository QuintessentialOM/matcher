using System.Diagnostics.CodeAnalysis;
using Mono.Cecil;

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

	public TypeInstance GetCreateGenericParameterTypeInstance(GenericParameter genericParameter) {
		return GetCreateGenericParameterTypeInstance(genericParameter, true)!;
	}

	public TypeInstance? GetCreateGenericParameterTypeInstance(GenericParameter genericParameter, bool createUnknown) {
		var key = Utils.GenericParameterPlaceholderName(genericParameter);
		if (types.ContainsKey(key)) return types[key];
		if (!createUnknown) return null;
		types[key] = new TypeInstance(this, genericParameter, !Matcher.IsTypeFullNameDeobfuscated(genericParameter));
		return types[key];
	}

	public TypeInstance GetCreateTypeInstance(TypeReference type) {
		return GetCreateTypeInstance(type, true)!;
	}

	public TypeInstance? GetCreateTypeInstance(TypeReference type, bool createUnknown) {
		if (type is GenericParameter genericParameter) {
			return GetCreateGenericParameterTypeInstance(genericParameter, createUnknown);
		}
		if (types.ContainsKey(type.FullName)) return types[type.FullName];
		if (!createUnknown) return null;
		types[type.FullName] = new TypeInstance(this, type, !Matcher.IsTypeFullNameDeobfuscated(type));
		return types[type.FullName];
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