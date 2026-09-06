using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Events;

public class EventDefinitionDto
{
	public string Id { get; set; } = string.Empty;

	public string ProviderId { get; set; } = string.Empty;

	public LocalizedText ProviderName { get; set; }

	public bool IsIntegration { get; set; }

	public LocalizedText Name { get; set; }

	public LocalizedText Description { get; set; }

	public LocalizedText Category { get; set; }

	public string? IconName { get; set; }

	public string DeliveryKind { get; set; } = "push";

	public List<ActionParameterDef> ConfigurationParameters { get; set; } = [];

	public List<ActionParameterDef> PayloadParameters { get; set; } = [];
}
