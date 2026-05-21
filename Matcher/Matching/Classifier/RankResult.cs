using System.Diagnostics.CodeAnalysis;

namespace Matcher.Matching.Classifier;

public class RankResult<T> {
	public required T subject { get; init; }
	public required double score { get; init; }
	public required List<ClassifierResult<T>> results { get; init; }

	[SetsRequiredMembers]
	public RankResult(T subject, double score, List<ClassifierResult<T>> results) {
		this.subject = subject;
		this.score = score;
		this.results = results;
	}
}
