using System.Diagnostics.CodeAnalysis;

namespace Matcher.Matching.Classifier;

[method: SetsRequiredMembers]
public class ClassifierResult<T>(IClassifier<T> classifier, double score)
{
	public required IClassifier<T> Classifier { get; init; } = classifier;
	public required double Score { get; init; } = score;

	override public string ToString() {
		return Classifier.GetName()+": "+Score;
	}
}
