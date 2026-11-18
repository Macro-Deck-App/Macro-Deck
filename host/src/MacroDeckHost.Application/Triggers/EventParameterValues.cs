using System.Collections;

namespace MacroDeckHost.Application.Triggers;

public static class EventParameterValues
{
	public static string? FindNonFinite(IReadOnlyDictionary<string, object?> parameters)
	{
		var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
		foreach (var (name, value) in parameters)
		{
			if (ContainsNonFinite(value, visited))
			{
				return name;
			}
		}

		return null;
	}

	private static bool ContainsNonFinite(object? value, HashSet<object> visited)
	{
		switch (value)
		{
			case double number:
				return !double.IsFinite(number);
			case float number:
				return !float.IsFinite(number);
			case null or string:
				return false;
			case IDictionary dictionary:
				return visited.Add(dictionary)
					&& dictionary.Values.Cast<object?>().Any(item => ContainsNonFinite(item, visited));
			case IEnumerable items:
				return visited.Add(items) && items.Cast<object?>().Any(item => ContainsNonFinite(item, visited));
			default:
				return false;
		}
	}
}
