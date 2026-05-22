using System.Diagnostics.CodeAnalysis;
using Mono.Cecil;

namespace Matcher.Matching;

public abstract class MatchableMemberOrClass : Matchable {
	public required MemberReference? CecilMemberReference { get; init; }
	private readonly string name;

	[SetsRequiredMembers]
	public MatchableMemberOrClass(LocalClassEnv env, MemberReference? cecilMemberReference, string name, bool isNameObfuscated) : base(env, isNameObfuscated) {
		CecilMemberReference = cecilMemberReference;
		this.name = name;
	}

	public override string GetName() {
		return name;
	}
}
