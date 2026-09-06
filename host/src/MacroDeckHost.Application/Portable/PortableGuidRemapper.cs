namespace MacroDeckHost.Application.Portable;

public static class PortableGuidRemapper
{
	public static string? Remap(string? data, IReadOnlyDictionary<Guid, Guid> idMap)
	{
		if (string.IsNullOrEmpty(data) || idMap.Count == 0)
		{
			return data;
		}

		var result = data;
		foreach (var (oldId, newId) in idMap)
		{
			if (oldId == newId)
			{
				continue;
			}

			result = result.Replace(oldId.ToString(), newId.ToString(), StringComparison.OrdinalIgnoreCase);
		}

		return result;
	}
}
