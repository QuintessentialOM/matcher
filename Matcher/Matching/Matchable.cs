using System.Diagnostics.CodeAnalysis;

namespace Matcher.Matching;

[method: SetsRequiredMembers]
public abstract class Matchable(LocalClassEnv env, bool isNameObfuscated) {
	public LocalClassEnv Env { get; init; } = env;
	public bool IsNameObfuscated { get; init; } = isNameObfuscated;

	// suggested name for intermediary->named mappings where there's a reasonable auto-generated name (assets from file path, etc.)
	public string? SuggestedMappedName { get; set; }

	// combination of name and other metadata e.g. method sig, field type
	public abstract string GetId();
	public abstract string GetName();

	public abstract bool IsMatchable();
	public abstract bool SetMatchable(bool matchable);

	public bool HasMatch() {
		return GetMatch() != null;
	}

	public abstract Matchable? GetMatch();
	// no base class SetMatch due to method parameter contravariance
}