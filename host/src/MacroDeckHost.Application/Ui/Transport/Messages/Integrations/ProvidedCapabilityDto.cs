using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

public class ProvidedCapabilityDto
{
	public string Kind { get; set; } = string.Empty;

	public LocalizedText Name { get; set; }
}
