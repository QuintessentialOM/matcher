namespace Matcher.Matching;

public class MethodHierarchyData {
	public MethodHierarchyData? MatchedHierarchy { get; set; }
	public bool matchable = true;
	public readonly ISet<MethodInstance> members = new HashSet<MethodInstance>();
}
