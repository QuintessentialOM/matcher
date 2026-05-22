namespace Matcher.Matching.Classifier;

public interface IClassifier<T> {
	string GetName();
	double GetWeight();
	double GetScore(T a, T b, MatchingEnv env);
}
