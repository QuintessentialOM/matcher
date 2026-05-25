using System.Diagnostics.CodeAnalysis;
using Mono.Cecil;

namespace Matcher.Matching;

public class MethodInstance : MatchableMember {
	public MethodDefinition? CecilMethod { get {
		return (MethodDefinition?) CecilMemberReference;
	} }
	private MethodInstance? matchedMethod;
	public MethodHierarchyData? hierarchyData;
	public readonly MethodParamInstance[] args;
	public TypeInstance returnType;

	public readonly HashSet<string> strings = [];

	public readonly HashSet<MethodInstance> refsIn = [];
	public readonly HashSet<MethodInstance> refsOut = [];
	public readonly HashSet<FieldInstance> fieldReadRefs = [];
	public readonly HashSet<FieldInstance> fieldWriteRefs = [];
	public readonly HashSet<TypeInstance> typeRefs = [];

	public readonly HashSet<MethodInstance> parents = [];
	public readonly HashSet<MethodInstance> children = [];

	[SetsRequiredMembers]
	public MethodInstance(LocalClassEnv env, TypeInstance containingType, MethodDefinition cecilMethod, int position, bool isNameObfuscated) : base(env, containingType, cecilMethod, position, isNameObfuscated) {
		MethodParamInstance[] args = new MethodParamInstance[cecilMethod.Parameters.Count];
		for (int i = 0; i < cecilMethod.Parameters.Count; i++) {
			var param = cecilMethod.Parameters[i];
			args[i] = new MethodParamInstance(env, this, param, !Matcher.NonObfuscatedPattern.IsMatch(param.Name), i);
			var argType = args[i].paramType;
			typeRefs.Add(argType);
			argType.methodTypeRefs.Add(this);
		}
		this.args = args;
		returnType = env.GetCreateTypeInstance(cecilMethod.ReturnType);
		typeRefs.Add(returnType);
		returnType.methodTypeRefs.Add(this);
	}

	public override string GetId() {
		return CecilMethod.Name; // TODO include desc
	}

	public override bool IsMatchable() {
		return hierarchyData != null && hierarchyData.matchable && ContainingType.IsMatchable();
	}

	public override bool SetMatchable(bool matchable) {
		if (!matchable && matchedMethod != null) return false;
		if (matchable && !ContainingType.IsMatchable()) return false;
		if (hierarchyData == null) return !matchable;
		if (!matchable && hierarchyData.MatchedHierarchy != null) return false;

		hierarchyData.matchable = matchable;

		return true;
	}

	public override MethodInstance? GetMatch() {
		return matchedMethod;
	}

	public void SetMatch(MethodInstance? method) {
		matchedMethod = method;
		// TODO it probably shouldn't be null?
		if (hierarchyData != null) hierarchyData!.MatchedHierarchy = matchedMethod?.hierarchyData;
	}

	public static string GetId(string name, string desc) {
		return name+desc;
	}

	public bool HasMatchedHierarchy(MethodInstance other) {
		return hierarchyData != null && hierarchyData.MatchedHierarchy == other.hierarchyData;
	}

	public bool HasHierarchyMatch() {
		return hierarchyData?.MatchedHierarchy != null;
	}
}
