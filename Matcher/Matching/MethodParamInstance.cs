using System.Diagnostics.CodeAnalysis;
using Mono.Cecil;

namespace Matcher.Matching;

public class MethodParamInstance : Matchable {
	public MethodInstance ContainingMethod { get; init; }
	public ParameterDefinition CecilParameter { get; init; }
	private MethodParamInstance? matchedMethodParam;
	private bool matchable = true;
	public TypeInstance paramType;

	[SetsRequiredMembers]
	public MethodParamInstance(LocalClassEnv env, MethodInstance containingMethod, ParameterDefinition cecilParameter, bool isNameObfuscated) : base(env, isNameObfuscated) {
		this.ContainingMethod = containingMethod;
		this.CecilParameter = cecilParameter;
		paramType = env.GetCreateTypeInstance(cecilParameter.ParameterType.Name);
	}
	
	public override string GetId() {
		throw new NotImplementedException();
	}
	
	public override string GetName() {
		throw new NotImplementedException();
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
