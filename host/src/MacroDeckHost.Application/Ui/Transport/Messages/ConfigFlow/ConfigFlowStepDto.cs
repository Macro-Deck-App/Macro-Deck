using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;

public class ConfigFlowStepDto
{
	public string StepId { get; set; } = string.Empty;

	public LocalizedText Title { get; set; }

	public LocalizedText Description { get; set; }

	public List<ConfigFlowCopyValueDto> Values { get; set; } = new();

	public List<ConfigFlowInstructionDto> Instructions { get; set; } = new();

	public List<ConfigFlowLinkDto> Links { get; set; } = new();

	public List<ActionParameterDef> Fields { get; set; } = new();

	public List<ActionParameterDef> AdvancedFields { get; set; } = new();
}
