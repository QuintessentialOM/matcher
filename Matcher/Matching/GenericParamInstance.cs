using System.Diagnostics.CodeAnalysis;
using Mono.Cecil;

namespace Matcher.Matching;

public class GenericParamInstance : MatchableMember {
	public GenericParameter? CecilGenericParam { get {
		return (GenericParameter?) CecilMemberReference;
	} }
	private GenericParamInstance? matchedGenericParam;

	[SetsRequiredMembers]
	public GenericParamInstance(LocalClassEnv env, TypeInstance containingType, GenericParameter cecilGenericParam, int position, bool isNameObfuscated) : base(env, containingType, cecilGenericParam, position, isNameObfuscated) {
	}
	
	// TODO should probably actually care about generic constraints but that's more effort
	public override string GetId() {
		return GetName();
	}

	public override bool HasPotentialMatch() {
		throw new NotImplementedException();
	}

	public override bool IsMatchable() {
		throw new NotImplementedException();
	}

	public override bool SetMatchable(bool matchable) {
		throw new NotImplementedException();
	}

	public override GenericParamInstance? GetMatch() {
		return matchedGenericParam;
	}

	public void SetMatch(GenericParamInstance? genericParam) {
		matchedGenericParam = genericParam;
	}

	public override Matchable GetOwner() {
		throw new NotImplementedException();
	}

	public override bool IsFullyMatched(bool recursive) {
		throw new NotImplementedException();
	}
}
