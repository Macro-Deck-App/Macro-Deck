using System.Text.RegularExpressions;

namespace MacroDeckHost.Application.Secrets;

public static partial class SecretReferences
{
	public static IReadOnlyCollection<Guid> Extract(string? widgetData)
	{
		if (string.IsNullOrEmpty(widgetData))
		{
			return [];
		}

		var ids = new HashSet<Guid>();
		foreach (Match match in ReferenceRegex().Matches(widgetData))
		{
			if (Guid.TryParse(match.Groups[1].Value, out var id))
			{
				ids.Add(id);
			}
		}

		return ids;
	}

	[GeneratedRegex(@"\\*""\$secret\\*""\s*:\s*\\*""([0-9a-fA-F-]{36})\\*""")]
	public static partial Regex ReferenceRegex();
}
