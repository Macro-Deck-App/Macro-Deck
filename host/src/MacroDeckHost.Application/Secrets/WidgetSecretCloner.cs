namespace MacroDeckHost.Application.Secrets;

public class WidgetSecretCloner : IWidgetSecretCloner
{
	private readonly ISecretService _secretService;

	public WidgetSecretCloner(ISecretService secretService)
	{
		_secretService = secretService;
	}

	public async Task<string?> CloneReferencedSecrets(string? widgetData)
	{
		if (string.IsNullOrEmpty(widgetData))
		{
			return widgetData;
		}

		var idMap = new Dictionary<Guid, Guid>();
		foreach (var originalId in SecretReferences.Extract(widgetData))
		{
			var cloneId = await _secretService.Clone(originalId);
			if (cloneId.HasValue)
			{
				idMap[originalId] = cloneId.Value;
			}
		}

		if (idMap.Count == 0)
		{
			return widgetData;
		}

		return SecretReferences.ReferenceRegex().Replace(widgetData,
			match =>
			{
				var original = match.Groups[1].Value;
				return Guid.TryParse(original, out var originalId) && idMap.TryGetValue(originalId, out var cloneId)
					? match.Value.Replace(original, cloneId.ToString(), StringComparison.Ordinal)
					: match.Value;
			});
	}
}
