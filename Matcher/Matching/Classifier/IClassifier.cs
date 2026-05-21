namespace Matcher.Matching.Classifier;

public interface IClassifier<T> {
	String getName();
	double getWeight();
	double getScore(T a, T b, MatchingEnv env);
}
