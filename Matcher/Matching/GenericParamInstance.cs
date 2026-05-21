using System.Diagnostics.CodeAnalysis;
using Mono.Cecil;

namespace Matcher.Matching;

public class GenericParamInstance : MatchableMember {
	public GenericParameter cecilGenericParam { get {
		return (GenericParameter) cecilMemberReference;
	} }
	private GenericParamInstance? matchedGenericParam;

	[SetsRequiredMembers]
	public GenericParamInstance(LocalClassEnv env, TypeInstance containingType, GenericParameter cecilGenericParam, bool isNameObfuscated) : base(env, containingType, cecilGenericParam, isNameObfuscated) {
	}
	
	// TODO should probably actually care about generic constraints but that's more effort
	public override string getId() {
		return getName();
	}

	public override bool hasPotentialMatch() {
		throw new NotImplementedException();
	}

	public override bool isMatchable() {
		throw new NotImplementedException();
	}

	public override bool setMatchable(bool matchable) {
		throw new NotImplementedException();
	}

	public override GenericParamInstance? getMatch() {
		return matchedGenericParam;
	}

	public void setMatch(GenericParamInstance? genericParam) {
		matchedGenericParam = genericParam;
	}

	public override Matchable getOwner() {
		throw new NotImplementedException();
	}

	public override bool isFullyMatched(bool recursive) {
		throw new NotImplementedException();
	}
}
