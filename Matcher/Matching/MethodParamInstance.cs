using System.Diagnostics.CodeAnalysis;
using Mono.Cecil;

namespace Matcher.Matching;

public class MethodParamInstance : Matchable {
	public MethodInstance containingMethod { get; init; }
	public ParameterDefinition cecilParameter { get; init; }
	private MethodParamInstance? matchedMethodParam;
	private bool matchable = true;
	public TypeInstance paramType;

	[SetsRequiredMembers]
	public MethodParamInstance(LocalClassEnv env, MethodInstance containingMethod, ParameterDefinition cecilParameter, bool isNameObfuscated) : base(env, isNameObfuscated) {
		this.containingMethod = containingMethod;
		this.cecilParameter = cecilParameter;
		paramType = env.getCreateTypeInstance(cecilParameter.ParameterType.Name);
	}
	
	public override string getId() {
		throw new NotImplementedException();
	}
	
	public override string getName() {
		throw new NotImplementedException();
	}

	public override bool hasPotentialMatch() {
		throw new NotImplementedException();
	}

	public override bool isMatchable() {
		return matchable && containingMethod.isMatchable();
	}

	public override bool setMatchable(bool matchable) {
		if (!matchable && matchedMethodParam != null) return false;
		if (matchable && !containingMethod.isMatchable()) return false;

		this.matchable = matchable;

		return true;
	}

	public override MethodParamInstance? getMatch() {
		return matchedMethodParam;
	}

	public void setMatch(MethodParamInstance? methodParam) {
		matchedMethodParam = methodParam;
	}

	public override Matchable getOwner() {
		throw new NotImplementedException();
	}

	public override bool isFullyMatched(bool recursive) {
		throw new NotImplementedException();
	}
}
