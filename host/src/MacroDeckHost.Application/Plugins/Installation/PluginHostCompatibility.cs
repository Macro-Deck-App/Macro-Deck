using MacroDeck.Plugin.Packaging.Versioning;

namespace MacroDeckHost.Application.Plugins.Installation;

public enum PluginIncompatibility
{
	Unknown,
	HostTooOld,
	HostTooNew
}

public static class PluginHostCompatibility
{
	// SemanticVersionRange only answers Satisfies, so the direction is found by asking whether any newer
	// Macro Deck would satisfy the range: the next releases, a far-future one, and every version it names.
	public static PluginIncompatibility ClassifyMacroDeckRange(string? range, string? hostVersion)
	{
		if (!SemanticVersionRange.TryParse(range, out var parsedRange) ||
			!SemanticVersion.TryParse(hostVersion, out var current))
		{
			return PluginIncompatibility.Unknown;
		}

		return NewerCandidates(range!, current).Any(parsedRange.Satisfies)
			? PluginIncompatibility.HostTooOld
			: PluginIncompatibility.HostTooNew;
	}

	private static IEnumerable<SemanticVersion> NewerCandidates(string range, SemanticVersion current)
	{
		var candidates = new List<string>
		{
			$"{current.Major}.{current.Minor}.{current.Patch}",
			$"{current.Major}.{current.Minor}.{current.Patch + 1}",
			$"{current.Major}.{current.Minor + 1}.0",
			$"{current.Major + 1}.0.0",
			"999.0.0"
		};
		candidates.AddRange(range.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(comparator => comparator.TrimStart('>', '<', '=').Trim()));

		foreach (var candidate in candidates)
		{
			if (SemanticVersion.TryParse(candidate, out var version) && version > current)
			{
				yield return version;
			}
		}
	}
}
