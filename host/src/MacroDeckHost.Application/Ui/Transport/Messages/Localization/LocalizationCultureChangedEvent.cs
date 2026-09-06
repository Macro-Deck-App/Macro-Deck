namespace MacroDeckHost.Application.Ui.Transport.Messages.Localization;

public class LocalizationCultureChangedEvent
{
	public string Culture { get; set; } = string.Empty;

	public string FallbackCulture { get; set; } = string.Empty;
}
