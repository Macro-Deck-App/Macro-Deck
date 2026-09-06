using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;

public class ConfigFlowInstructionDto
{
	public LocalizedText Text { get; set; }

	public List<ConfigFlowCopyValueDto> Values { get; set; } = new();
}
