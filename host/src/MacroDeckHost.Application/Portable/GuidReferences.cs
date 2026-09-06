using System.Text.RegularExpressions;

namespace MacroDeckHost.Application.Portable;

public static partial class GuidReferences
{
	public static IReadOnlyCollection<Guid> ExtractAll(string? data)
	{
		if (string.IsNullOrEmpty(data))
		{
			return [];
		}

		var ids = new HashSet<Guid>();
		foreach (Match match in GuidRegex().Matches(data))
		{
			if (Guid.TryParse(match.Value, out var id))
			{
				ids.Add(id);
			}
		}

		return ids;
	}

	[GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
	private static partial Regex GuidRegex();
}
