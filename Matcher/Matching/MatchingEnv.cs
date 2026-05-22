using System.Diagnostics.CodeAnalysis;

namespace Matcher.Matching;

public class MatchingEnv {
	public required LocalClassEnv EnvA { get; init; }
	public required LocalClassEnv EnvB { get; init; }

	[SetsRequiredMembers]
	public MatchingEnv() {
		EnvA = new(this);
		EnvB = new(this);
		EnvA.SetOther(EnvB);
		EnvB.SetOther(EnvA);
	}
}
