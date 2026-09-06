using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;

public class ConfigFlowCopyValueDto
{
	public LocalizedText Label { get; set; }

	public string Value { get; set; } = string.Empty;
}
