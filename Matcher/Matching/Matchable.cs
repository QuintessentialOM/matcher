using System.Diagnostics.CodeAnalysis;

namespace Matcher.Matching;

// public enum MatchableKind {
// 	CLASS, FIELD, METHOD, METHOD_ARG, METHOD_VAR
// }

public abstract class Matchable {
	public LocalClassEnv env { get; init; }
	public bool isNameObfuscated { get; init; }

	[SetsRequiredMembers]
	public Matchable(LocalClassEnv env, bool isNameObfuscated) {
		this.env = env;
		this.isNameObfuscated = isNameObfuscated;
	}

	// public abstract MatchableKind getKind();

	// combination of name and other metadata e.g. method sig, field type
	public abstract string getId();
	public abstract string getName();
	// public abstract string getName(NameType type);

	// string getDisplayName(NameType type, bool full) {
	// 	return getName(type);
	// }

	// public abstract bool hasMappedName();
	// public abstract bool hasLocalTmpName();
	// public abstract bool hasAuxName(int index);

	// public abstract string getMappedComment();
	// public abstract void setMappedComment(string comment);

	public abstract Matchable getOwner();
	// public abstract ClassEnv getEnv();

	// public abstract int getUid();

	public abstract bool hasPotentialMatch();

	public abstract bool isMatchable();
	public abstract bool setMatchable(bool matchable);

	public bool hasMatch() {
		return getMatch() != null;
	}

	public abstract Matchable? getMatch();
	// no base class setMatch due to method parameter contravariance


	public abstract bool isFullyMatched(bool recursive);
	// public abstract float getSimilarity();
}