using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;

public class ConfigFlowLinkDto
{
	public LocalizedText Label { get; set; }

	public string Url { get; set; } = string.Empty;
}
