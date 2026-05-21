using System.Linq;
using System.Runtime.CompilerServices;

namespace Matcher;

// put in more sensible places later maybe

public sealed class IdentityEqualityComparer<T> : IEqualityComparer<T> where T : class {
	public int GetHashCode(T value) {
		return RuntimeHelpers.GetHashCode(value);
	}

	public bool Equals(T? left, T? right) {
		return ReferenceEquals(left, right);
	}
}

public static class Utils {
	public static T[] CopyArray<T>(T[] data, int length) {
		T[] result = new T[length];
		Array.Copy(data, 0, result, 0, length);
		return result;
	}

	public static IEnumerable<(int index, T item)> WithIndex<T>(this IEnumerable<T> self)       
       => self.Select((item, index) => (index, item));
}
