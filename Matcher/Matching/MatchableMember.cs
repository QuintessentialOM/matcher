using System.Diagnostics.CodeAnalysis;
using Mono.Cecil;

namespace Matcher.Matching;

public abstract class MatchableMember : MatchableMemberOrClass {
	public TypeInstance containingType { get; init; }

	[SetsRequiredMembers]
	public MatchableMember(LocalClassEnv env, TypeInstance containingType, MemberReference cecilMemberReference, bool isNameObfuscated) : base(env, cecilMemberReference, cecilMemberReference.Name, isNameObfuscated) {
		this.containingType = containingType;
	}
}
