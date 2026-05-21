using System.Diagnostics.CodeAnalysis;
using Mono.Cecil;

namespace Matcher.Matching;

public class FieldInstance : MatchableMember {
	public FieldDefinition cecilField { get {
		return (FieldDefinition) cecilMemberReference;
	} }
	private readonly string id;
	private FieldInstance? matchedField;
	private bool matchable = true;
	public TypeInstance fieldType;

	[SetsRequiredMembers]
	public FieldInstance(LocalClassEnv env, TypeInstance containingType, FieldDefinition cecilField, bool isNameObfuscated) : base(env, containingType, cecilField, isNameObfuscated) {
		id = getId(cecilField.Name, cecilField.FieldType.Name);
		fieldType = env.getCreateTypeInstance(cecilField.FieldType.Name);
	}
	
	public override string getId() {
		return id;
	}

	public override bool hasPotentialMatch() {
		throw new NotImplementedException();
	}

	public override bool isMatchable() {
		return matchable && containingType.isMatchable();
	}

	public override bool setMatchable(bool matchable) {
		if (!matchable && matchedField != null) return false;
		if (matchable && !containingType.isMatchable()) return false;

		this.matchable = matchable;

		return true;
	}

	public override FieldInstance? getMatch() {
		return matchedField;
	}

	public void setMatch(FieldInstance? field) {
		matchedField = field;
	}

	public override Matchable getOwner() {
		throw new NotImplementedException();
	}

	public override bool isFullyMatched(bool recursive) {
		throw new NotImplementedException();
	}

	public static string getId(string name, string desc) {
		return name + ";;" + desc;
	}
}
