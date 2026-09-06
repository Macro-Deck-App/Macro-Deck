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
}
