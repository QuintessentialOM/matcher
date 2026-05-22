using System.Diagnostics.CodeAnalysis;
using Mono.Cecil;

namespace Matcher.Matching;

public abstract class MatchableMember : MatchableMemberOrClass {
	public TypeInstance ContainingType { get; init; }
	public int Position { get; init; }

	[SetsRequiredMembers]
	public MatchableMember(LocalClassEnv env, TypeInstance containingType, MemberReference cecilMemberReference, int position, bool isNameObfuscated) : base(env, cecilMemberReference, cecilMemberReference.Name, isNameObfuscated) {
		this.ContainingType = containingType;
		this.Position = position;
	}
}
