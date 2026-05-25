using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Matcher.Matching.Classifier;
using Mono.Cecil;

namespace Matcher.Matching;

public class TypeInstance : MatchableMemberOrClass {
	public TypeReference CecilTypeReference { get {
		return (TypeReference) CecilMemberReference!;
	} }
	public TypeDefinition? CecilType { get {
		return CecilMemberReference is TypeDefinition def ? def : null;
	} }
	public readonly Dictionary<string, MethodInstance> methodsById = [];
	public readonly Dictionary<string, FieldInstance> fieldsById = [];
	public readonly Dictionary<string, GenericParamInstance> genericParamsById = [];

	public readonly List<MethodInstance> methodsOrdered = [];
	public readonly List<FieldInstance> fieldsOrdered = [];
	public readonly List<GenericParamInstance> genericParamsOrdered = [];
	
	private bool matchable = true;
	private TypeInstance? matchedType;

	public readonly TypeInstance? elementType; // type of array elements, for array types
	public List<TypeInstance> arrays = []; // array types with this type as an element, for non-array types

	public readonly HashSet<string> strings = [];

	public TypeInstance? baseType;
	public readonly List<TypeInstance> childTypes = [];

	public TypeInstance? outerType;
	public readonly List<TypeInstance> nestedTypes = [];

	public readonly List<TypeInstance> interfaces = [];
	public readonly List<TypeInstance> implementedBy = [];

	public readonly HashSet<MethodInstance> methodTypeRefs = [];
	public readonly HashSet<FieldInstance> fieldTypeRefs = [];

	// position of nested types within their outer type. unused on non-nested types, indicated by -1
	public int position = -1;

	[SetsRequiredMembers]
	public TypeInstance(LocalClassEnv env, TypeReference cecilType, bool isNameObfuscated) : this(env, cecilType, cecilType.Name, isNameObfuscated) {
	}

	private static readonly Regex ArrayPattern = new Regex(Regex.Escape("[]"));

	[SetsRequiredMembers]
	private TypeInstance(LocalClassEnv env, TypeReference cecilType, string name, bool isNameObfuscated) : base(env, cecilType, name, isNameObfuscated) {
		int arrayDimensions = ArrayPattern.Count(name);
		if (arrayDimensions > 0) {
			var elementName = name.TrimEnd(['[', ']']);
			// TODO does GetElementType give desired behavior
			elementType = env.GetCreateTypeInstance(cecilType.GetElementType());
			elementType.arrays.Add(this);
		}
	}
	
	public override string GetId() {
		return GetName();
	}

	public bool IsReal() {
		return CecilType != null;
	}

	public override bool HasPotentialMatch() {
		if (matchedType != null) return true;
		if (!IsMatchable()) return false;

		foreach (var o in Env.GetOther().types.Values) {
			if (o.IsReal() && ClassifierUtil.CheckPotentialEquality(this, o)) return true;
		}

		return false;
	}

	public override bool IsMatchable() {
		return matchable;
	}

	public override bool SetMatchable(bool matchable) {
		if (!matchable && matchedType != null) return false;

		this.matchable = matchable;

		return true;
	}

	public override TypeInstance? GetMatch() {
		return matchedType;
	}

	public void SetMatch(TypeInstance? type) {
		matchedType = type;
	}

	public override bool IsFullyMatched(bool recursive) {
		throw new NotImplementedException();
	}

	public override Matchable GetOwner() {
		throw new NotImplementedException();
	}

	public bool IsArray() {
		return elementType != null;
	}

	public int GetArrayDimensions() {
		if (elementType == null) return 0;

		return ArrayPattern.Count(GetName());
	}

	public TypeSubgroup GetSubgroup() {
		if (CecilType == null) return TypeSubgroup.Normal; // TODO is this valid? maybe not? idk?
		if (CecilType.IsEnum) return TypeSubgroup.Enum;
		if (CecilType.BaseType?.FullName?.Equals("System.MulticastDelegate") ?? false) return TypeSubgroup.Delegate;
		return TypeSubgroup.Normal;
	}

	public MethodInstance? GetMethod(string name, string? desc) {
		if (desc != null) {
			return methodsById!.GetValueOrDefault(MethodInstance.GetId(name, desc), null);
		} else {
			MethodInstance? ret = null;

			foreach (MethodInstance method in methodsById.Values) {
				if (method.GetName().Equals(name)) {
					if (ret != null) return null; // non-unique

					ret = method;
				}
			}

			return ret;
		}
	}

	public FieldInstance? GetField(string name, string? desc) {
		if (desc != null) {
			return fieldsById!.GetValueOrDefault(FieldInstance.GetId(name, desc), null);
		} else {
			FieldInstance? ret = null;

			foreach (FieldInstance field in fieldsById.Values) {
				if (field.GetName().Equals(name)) {
					if (ret != null) return null; // non-unique

					ret = field;
				}
			}

			return ret;
		}
	}
}
