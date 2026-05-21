using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Matcher.Matching.Classifier;
using Mono.Cecil;

namespace Matcher.Matching;

public class TypeInstance : MatchableMemberOrClass {
	public TypeDefinition? cecilType { get {
		return (TypeDefinition?) cecilMemberReference;
	} }
	public readonly Dictionary<string, MethodInstance> methodsById = [];
	public readonly Dictionary<string, FieldInstance> fieldsById = [];
	public readonly Dictionary<string, GenericParamInstance> genericParamsById = [];
	private bool matchable = true;
	private TypeInstance? matchedType;

	public readonly TypeInstance? elementType; // type of array elements, for array types
	public List<TypeInstance> arrays = []; // array types with this type as an element, for non-array types

	public TypeInstance? baseType;
	public readonly List<TypeInstance> childTypes = [];

	public TypeInstance? outerType;
	public readonly List<TypeInstance> nestedTypes = [];

	public readonly List<TypeInstance> interfaces = [];
	public readonly List<TypeInstance> implementedBy = [];

	[SetsRequiredMembers]
	public TypeInstance(LocalClassEnv env, TypeDefinition cecilType, bool isNameObfuscated) : this(env, cecilType, cecilType.Name, isNameObfuscated) {
	}

	[SetsRequiredMembers]
	public TypeInstance(LocalClassEnv env, string name, bool isNameObfuscated) : this(env, null, name, isNameObfuscated) {
	}

	private static readonly Regex ArrayPattern = new Regex(Regex.Escape("[]"));

	[SetsRequiredMembers]
	private TypeInstance(LocalClassEnv env, TypeDefinition? cecilType, string name, bool isNameObfuscated) : base(env, cecilType, name, isNameObfuscated) {
		int arrayDimensions = ArrayPattern.Count(name);
		if (arrayDimensions > 0) {
			var elementName = name.TrimEnd(['[', ']']);
			elementType = env.getCreateTypeInstance(elementName);
			elementType.arrays.Add(this);
		}
	}
	
	public override string getId() {
		return getName();
	}

	public bool isReal() {
		return cecilType != null;
	}

	public override bool hasPotentialMatch() {
		if (matchedType != null) return true;
		if (!isMatchable()) return false;

		foreach (var o in env.getOther().types.Values) {
			if (o.isReal() && ClassifierUtil.checkPotentialEquality(this, o)) return true;
		}

		return false;
	}

	public override bool isMatchable() {
		return matchable;
	}

	public override bool setMatchable(bool matchable) {
		if (!matchable && matchedType != null) return false;

		this.matchable = matchable;

		return true;
	}

	public override TypeInstance? getMatch() {
		return matchedType;
	}

	public void setMatch(TypeInstance? type) {
		matchedType = type;
	}

	public override bool isFullyMatched(bool recursive) {
		throw new NotImplementedException();
	}

	public override Matchable getOwner() {
		throw new NotImplementedException();
	}

	public bool isArray() {
		return elementType != null;
	}

	public int getArrayDimensions() {
		if (elementType == null) return 0;

		return ArrayPattern.Count(getName());
	}

	public MethodInstance? getMethod(string name, string? desc) {
		if (desc != null) {
			return methodsById[MethodInstance.getId(name, desc)];
		} else {
			MethodInstance? ret = null;

			foreach (MethodInstance method in methodsById.Values) {
				if (method.getName().Equals(name)) {
					if (ret != null) return null; // non-unique

					ret = method;
				}
			}

			return ret;
		}
	}

	public FieldInstance? getField(string name, string? desc) {
		if (desc != null) {
			return fieldsById[FieldInstance.getId(name, desc)];
		} else {
			FieldInstance? ret = null;

			foreach (FieldInstance field in fieldsById.Values) {
				if (field.getName().Equals(name)) {
					if (ret != null) return null; // non-unique

					ret = field;
				}
			}

			return ret;
		}
	}
}
