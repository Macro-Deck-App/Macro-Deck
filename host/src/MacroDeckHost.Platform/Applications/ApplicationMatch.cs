namespace MacroDeckHost.Platform.Applications;

internal static class ApplicationMatch
{
	public static IReadOnlySet<int> FindProcessIds(IReadOnlyList<RunningProcess> snapshot, string configured)
	{
		if (ApplicationTarget.Resolve(configured) is not { } target)
		{
			return new HashSet<int>();
		}

		var matches = new HashSet<int>();
		foreach (var process in snapshot)
		{
			// An elevated process hides its path, so a path entry still counts it by its file name.
			var matched = process.Path is { } path
				? target.MatchesPath(path) || !target.IsPath && target.MatchesName(process.Name)
				: target.MatchesName(process.Name);
			if (matched)
			{
				matches.Add(process.Id);
			}
		}

		return matches;
	}

	public static IReadOnlySet<int> FindSameApplication(
		IReadOnlyList<RunningProcess> snapshot,
		int processId,
		string? path,
		string? name)
	{
		var focused = snapshot.FirstOrDefault(process => process.Id == processId)
			?? new RunningProcess(processId, name ?? string.Empty, path);
		if (focused.Path is null && focused.Name.Length == 0)
		{
			return new HashSet<int>();
		}

		return snapshot.Where(process => IsSameApplication(focused, process)).Select(process => process.Id).ToHashSet();
	}

	private static bool IsSameApplication(RunningProcess focused, RunningProcess candidate)
	{
		if (focused.Path is { } path && candidate.Path is { } other)
		{
			return string.Equals(ApplicationTarget.Normalize(other),
				ApplicationTarget.Normalize(path),
				StringComparison.OrdinalIgnoreCase);
		}

		return string.Equals(candidate.Name, focused.Name, StringComparison.OrdinalIgnoreCase);
	}
}
