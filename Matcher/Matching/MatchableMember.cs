using System.Diagnostics.CodeAnalysis;
using Mono.Cecil;

namespace Matcher.Matching;

public abstract class MatchableMember : MatchableMemberOrClass {
	public TypeInstance containingType { get; init; }
	public int position { get; init; }

	[SetsRequiredMembers]
	public MatchableMember(LocalClassEnv env, TypeInstance containingType, MemberReference cecilMemberReference, int position, bool isNameObfuscated) : base(env, cecilMemberReference, cecilMemberReference.Name, isNameObfuscated) {
		this.containingType = containingType;
		this.position = position;
	}
}
