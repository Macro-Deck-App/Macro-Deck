namespace MacroDeckHost.Application.Secrets;

public interface IWidgetSecretScrubber
{
	Task ScrubReferencedSecrets(string? widgetData);
}
