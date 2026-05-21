using System.Diagnostics.CodeAnalysis;

namespace Matcher.Matching;

public class MatchingEnv {
	public required LocalClassEnv envA { get; init; }
	public required LocalClassEnv envB { get; init; }

	[SetsRequiredMembers]
	public MatchingEnv() {
		envA = new(this);
		envB = new(this);
		envA.setOther(envB);
		envB.setOther(envA);
	}
}
