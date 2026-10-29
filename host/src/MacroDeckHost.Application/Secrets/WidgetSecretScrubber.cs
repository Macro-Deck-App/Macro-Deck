namespace MacroDeckHost.Application.Secrets;

public class WidgetSecretScrubber : IWidgetSecretScrubber
{
	private readonly ISecretService _secretService;
	private readonly SecretScrubDeferral? _deferral;

	public WidgetSecretScrubber(ISecretService secretService, SecretScrubDeferral? deferral = null)
	{
		_secretService = secretService;
		_deferral = deferral;
	}

	public async Task ScrubReferencedSecrets(string? widgetData)
	{
		if (string.IsNullOrEmpty(widgetData))
		{
			return;
		}

		if (_deferral?.Active == true)
		{
			_deferral.Defer(SecretReferences.Extract(widgetData));
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
		if (_deferral?.Active == true)
		{
			_deferral.Defer(candidates);
			return;
		}

		foreach (var secretId in candidates)
		{
			await _secretService.Delete(secretId);
		}
	}
}
