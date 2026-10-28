namespace MacroDeckHost.Application.Secrets;

public interface IWidgetSecretScrubber
{
	Task ScrubReferencedSecrets(string? widgetData);

	Task ScrubUnreferencedSecrets(IEnumerable<string?> removedWidgetData, IEnumerable<string?> remainingWidgetData);
}
