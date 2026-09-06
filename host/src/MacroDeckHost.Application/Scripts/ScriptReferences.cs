using MacroDeckHost.Application.Portable;

namespace MacroDeckHost.Application.Scripts;

public static class ScriptReferences
{
	public static IReadOnlyCollection<Guid> Extract(string? json, IReadOnlySet<Guid> knownScriptIds)
	{
		if (knownScriptIds.Count == 0)
		{
			return [];
		}

		var referenced = GuidReferences.ExtractAll(json)
			.Where(knownScriptIds.Contains)
			.ToList();

		return referenced;
	}

	public static HashSet<Guid> ExpandTransitively(
		IEnumerable<Guid> directlyReferenced,
		IReadOnlySet<Guid> knownScriptIds,
		Func<Guid, string?> flowsById)
	{
		var resolved = new HashSet<Guid>();
		var pending = new Queue<Guid>(directlyReferenced);

		while (pending.Count > 0)
		{
			var scriptId = pending.Dequeue();
			if (!resolved.Add(scriptId))
			{
				continue;
			}

			foreach (var nested in Extract(flowsById(scriptId), knownScriptIds))
			{
				if (!resolved.Contains(nested))
				{
					pending.Enqueue(nested);
				}
			}
		}

		return resolved;
	}
}
