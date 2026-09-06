using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Backups;

public sealed record BackupDependencyWarning(
	BackupComponentGroup Group,
	BackupComponentGroup MissingDependency);

public sealed record BackupSelectionPlan(
	IReadOnlyList<BackupComponentGroup> Effective,
	IReadOnlyList<BackupComponentGroup> AutoSelected,
	IReadOnlyList<BackupDependencyWarning> Warnings);

public static class BackupComponentSelection
{
	/// <summary>
	/// Expands a selection over its transitive dependencies. Groups the caller explicitly excluded stay
	/// excluded and surface as warnings instead: a restore that leaves out icons or secrets is degraded,
	/// not invalid, and the user is allowed to make that choice.
	/// </summary>
	public static BackupSelectionPlan Expand(
		IReadOnlyCollection<BackupComponentGroup> selected,
		IReadOnlyCollection<BackupComponentGroup>? excluded = null)
	{
		var effective = new HashSet<BackupComponentGroup>(selected);
		var explicitlyExcluded = excluded is null
			? []
			: new HashSet<BackupComponentGroup>(excluded.Where(group => !selected.Contains(group)));

		var queue = new Queue<BackupComponentGroup>(selected);
		var autoSelected = new List<BackupComponentGroup>();
		var warnings = new List<BackupDependencyWarning>();

		while (queue.Count > 0)
		{
			var current = queue.Dequeue();

			foreach (var dependency in BackupComponentGroups.Definition(current).Requires)
			{
				if (effective.Contains(dependency))
				{
					continue;
				}

				if (explicitlyExcluded.Contains(dependency))
				{
					warnings.Add(new BackupDependencyWarning(current, dependency));
					continue;
				}

				effective.Add(dependency);
				autoSelected.Add(dependency);
				queue.Enqueue(dependency);
			}
		}

		var ordered = BackupComponentGroups.AllIds.Where(effective.Contains).ToList();

		return new BackupSelectionPlan(ordered, autoSelected, warnings);
	}
}
