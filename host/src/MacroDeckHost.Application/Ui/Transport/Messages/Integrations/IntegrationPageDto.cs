using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

public class IntegrationPageDto
{
	public string Id { get; set; } = string.Empty;

	public LocalizedText Title { get; set; }

	public string? Icon { get; set; }
}
