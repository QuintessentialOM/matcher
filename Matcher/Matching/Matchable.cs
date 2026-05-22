using System.Diagnostics.CodeAnalysis;

namespace Matcher.Matching;

// public enum MatchableKind {
// 	CLASS, FIELD, METHOD, METHOD_ARG, METHOD_VAR
// }

public abstract class Matchable {
	public LocalClassEnv Env { get; init; }
	public bool IsNameObfuscated { get; init; }

	[SetsRequiredMembers]
	public Matchable(LocalClassEnv env, bool isNameObfuscated) {
		Env = env;
		IsNameObfuscated = isNameObfuscated;
	}

	// public abstract MatchableKind getKind();

	// combination of name and other metadata e.g. method sig, field type
	public abstract string GetId();
	public abstract string GetName();
	// public abstract string getName(NameType type);

	// string getDisplayName(NameType type, bool full) {
	// 	return getName(type);
	// }

	// public abstract bool hasMappedName();
	// public abstract bool hasLocalTmpName();
	// public abstract bool hasAuxName(int index);

	// public abstract string getMappedComment();
	// public abstract void setMappedComment(string comment);

	public abstract Matchable GetOwner();
	// public abstract ClassEnv getEnv();

	// public abstract int getUid();

	public abstract bool HasPotentialMatch();

	public abstract bool IsMatchable();
	public abstract bool SetMatchable(bool matchable);

	public bool HasMatch() {
		return GetMatch() != null;
	}

	public abstract Matchable? GetMatch();
	// no base class setMatch due to method parameter contravariance


	public abstract bool IsFullyMatched(bool recursive);
	// public abstract float getSimilarity();
}