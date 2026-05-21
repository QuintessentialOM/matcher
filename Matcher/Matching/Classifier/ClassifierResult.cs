using System.Diagnostics.CodeAnalysis;

namespace Matcher.Matching.Classifier;

public class ClassifierResult<T> {
	public required IClassifier<T> classifier { get; init; }
	public required double score { get; init; }

	[SetsRequiredMembers]
	public ClassifierResult(IClassifier<T> classifier, double score) {
		this.classifier = classifier;
		this.score = score;
	}

	override public string ToString() {
		return classifier.getName()+": "+score;
	}
}
