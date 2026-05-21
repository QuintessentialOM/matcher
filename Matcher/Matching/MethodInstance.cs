using System.Diagnostics.CodeAnalysis;
using Mono.Cecil;

namespace Matcher.Matching;

public class MethodInstance : MatchableMember {
	public MethodDefinition cecilMethod { get {
		return (MethodDefinition) cecilMemberReference;
	} }
	private MethodInstance? matchedMethod;
	public MethodHierarchyData? hierarchyData;
	public readonly MethodParamInstance[] args;
	public TypeInstance returnType;

	[SetsRequiredMembers]
	public MethodInstance(LocalClassEnv env, TypeInstance containingType, MethodDefinition cecilMethod, bool isNameObfuscated) : base(env, containingType, cecilMethod, isNameObfuscated) {
		MethodParamInstance[] args = new MethodParamInstance[cecilMethod.Parameters.Count];
		for (int i = 0; i < cecilMethod.Parameters.Count; i++) {
			var param = cecilMethod.Parameters[i];
			args[i] = new MethodParamInstance(env, this, param, !Matcher.NonObfuscatedPattern.IsMatch(param.Name));
		}
		this.args = args;
		returnType = env.getCreateTypeInstance(cecilMethod.ReturnType.Name);
	}

	public override string getId() {
		return cecilMethod.Name; // TODO include desc
	}

	public override bool hasPotentialMatch() {
		throw new NotImplementedException();
	}

	public override bool isMatchable() {
		return hierarchyData != null && hierarchyData.matchable && containingType.isMatchable();
	}

	public override bool setMatchable(bool matchable) {
		if (!matchable && matchedMethod != null) return false;
		if (matchable && !containingType.isMatchable()) return false;
		if (hierarchyData == null) return !matchable;
		if (!matchable && hierarchyData.matchedHierarchy != null) return false;

		hierarchyData.matchable = matchable;

		return true;
	}

	public override MethodInstance? getMatch() {
		return matchedMethod;
	}

	public void setMatch(MethodInstance? method) {
		matchedMethod = method;
		// TODO it probably shouldn't be null?
		if (hierarchyData != null) hierarchyData!.matchedHierarchy = matchedMethod?.hierarchyData;
	}

	public override Matchable getOwner() {
		throw new NotImplementedException();
	}

	public override bool isFullyMatched(bool recursive) {
		throw new NotImplementedException();
	}

	public static string getId(string name, string desc) {
		return name+desc;
	}

	public bool hasMatchedHierarchy(MethodInstance other) {
		return hierarchyData != null && hierarchyData.matchedHierarchy == other.hierarchyData;
	}

	public bool hasHierarchyMatch() {
		return hierarchyData?.matchedHierarchy != null;
	}
}
