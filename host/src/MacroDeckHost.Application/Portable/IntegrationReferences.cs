namespace MacroDeckHost.Application.Portable;

public static class IntegrationReferences
{
	public static IReadOnlyList<string> Extract(IEnumerable<string?> data, IReadOnlyList<string> knownIntegrationIds)
	{
		if (knownIntegrationIds.Count == 0)
		{
			return [];
		}

		var blobs = data.Where(blob => !string.IsNullOrEmpty(blob)).ToList();
		if (blobs.Count == 0)
		{
			return [];
		}

		return knownIntegrationIds
			.Where(id => blobs.Any(blob => blob!.Contains(id, StringComparison.Ordinal)))
			.ToList();
	}
}
