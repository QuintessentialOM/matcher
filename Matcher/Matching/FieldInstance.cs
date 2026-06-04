using System.Diagnostics.CodeAnalysis;
using Mono.Cecil;

namespace Matcher.Matching;

public class FieldInstance : MatchableMember {
	public FieldDefinition? CecilField { get {
		return (FieldDefinition?) CecilMemberReference;
	} }
	private readonly string id;
	private FieldInstance? matchedField;
	private bool matchable = true;
	public TypeInstance fieldType;

	public readonly HashSet<MethodInstance> readRefs = [];
	public readonly HashSet<MethodInstance> writeRefs = [];

	[SetsRequiredMembers]
	public FieldInstance(LocalClassEnv env, TypeInstance containingType, FieldDefinition cecilField, int position, bool isNameObfuscated) : base(env, containingType, cecilField, position, isNameObfuscated) {
		id = GetId(cecilField.Name, cecilField.FieldType.Name);
		fieldType = env.GetCreateTypeInstance(cecilField.FieldType);
		fieldType.fieldTypeRefs.Add(this);
	}
	
	public override string GetId() {
		return id;
	}

	public override bool IsMatchable() {
		return matchable && ContainingType.IsMatchable();
	}

	public override bool SetMatchable(bool matchable) {
		if (!matchable && matchedField != null) return false;
		if (matchable && !ContainingType.IsMatchable()) return false;

		this.matchable = matchable;

		return true;
	}

	public override FieldInstance? GetMatch() {
		return matchedField;
	}

	public void SetMatch(FieldInstance? field) {
		if (field != null && Env == field.Env) throw new Exception("trying to match with field instance in same env");
		matchedField = field;
	}

	public static string GetId(string name, string desc) {
		return name + ";;" + desc;
	}
}
