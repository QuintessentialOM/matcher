namespace Matcher.Matching;

public class MethodHierarchyData {
	public MethodHierarchyData? matchedHierarchy { get; set; }
	public bool matchable = true;
	public readonly ISet<MethodInstance> members = new HashSet<MethodInstance>();
}
