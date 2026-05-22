using System.Diagnostics.CodeAnalysis;

namespace Matcher.Matching.Classifier;

[method: SetsRequiredMembers]
public class RankResult<T>(T subject, double score, List<ClassifierResult<T>> results)
{
	public required T Subject { get; init; } = subject;
	public required double Score { get; init; } = score;
	public required List<ClassifierResult<T>> Results { get; init; } = results;
}
