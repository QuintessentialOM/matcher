using System.Diagnostics.CodeAnalysis;
using Mono.Cecil;

namespace Matcher.Matching;

public class MethodParamInstance : Matchable {
	public MethodInstance ContainingMethod { get; init; }
	public ParameterDefinition CecilParameter { get; init; }
	private MethodParamInstance? matchedMethodParam;
	private bool matchable = true;
	public TypeInstance paramType;
	public readonly int position;

	[SetsRequiredMembers]
	public MethodParamInstance(LocalClassEnv env, MethodInstance containingMethod, ParameterDefinition cecilParameter, bool isNameObfuscated, int position) : base(env, isNameObfuscated) {
		this.ContainingMethod = containingMethod;
		this.CecilParameter = cecilParameter;
		this.position = position;
		paramType = env.GetCreateTypeInstance(cecilParameter.ParameterType.Name);
	}
	
	public override string GetId() {
		return CecilParameter.Name; // TODO include type
	}
	
	public override string GetName() {
		return CecilParameter.Name;
	}

	public override bool HasPotentialMatch() {
		throw new NotImplementedException();
	}

	public override bool IsMatchable() {
		return matchable && ContainingMethod.IsMatchable();
	}

	public override bool SetMatchable(bool matchable) {
		if (!matchable && matchedMethodParam != null) return false;
		if (matchable && !ContainingMethod.IsMatchable()) return false;

		this.matchable = matchable;

		return true;
	}

	public override MethodParamInstance? GetMatch() {
		return matchedMethodParam;
	}

	public void SetMatch(MethodParamInstance? methodParam) {
		matchedMethodParam = methodParam;
	}

	public override Matchable GetOwner() {
		throw new NotImplementedException();
	}

	public override bool IsFullyMatched(bool recursive) {
		throw new NotImplementedException();
	}
}
