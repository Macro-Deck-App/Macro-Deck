namespace MacroDeckHost.Application.Secrets;

public class WidgetSecretScrubber : IWidgetSecretScrubber
{
	private readonly ISecretService _secretService;

	public WidgetSecretScrubber(ISecretService secretService)
	{
		_secretService = secretService;
	}

	public async Task ScrubReferencedSecrets(string? widgetData)
	{
		if (string.IsNullOrEmpty(widgetData))
		{
			return;
		}

		foreach (var secretId in SecretReferences.Extract(widgetData))
		{
			await _secretService.Delete(secretId);
		}
	}

	public async Task ScrubUnreferencedSecrets(
		IEnumerable<string?> removedWidgetData,
		IEnumerable<string?> remainingWidgetData)
	{
		var candidates = removedWidgetData.SelectMany(SecretReferences.Extract).ToHashSet();
		if (candidates.Count == 0)
		{
			return;
		}

		candidates.ExceptWith(remainingWidgetData.SelectMany(SecretReferences.Extract));
		foreach (var secretId in candidates)
		{
			await _secretService.Delete(secretId);
		}
	}
}
