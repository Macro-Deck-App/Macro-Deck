using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

public class IntegrationActionCapability
{
	public string Id { get; set; } = string.Empty;

	public LocalizedText Name { get; set; }

	public LocalizedText Description { get; set; }

	public int ParameterCount { get; set; }

	public List<string> ParameterSummary { get; set; } = new();

	/// <summary>Whether this action's configured instance can drive a button's state (issue #612).</summary>
	public bool IsStateProviderAction { get; set; }

	/// <summary>Whether this action's configured instance can drive a widget's icon (issue #425).
	/// Independent of <see cref="IsStateProviderAction" /> - an action may implement neither, either, or
	/// both capabilities.</summary>
	public bool IsIconProviderAction { get; set; }

	public CapabilityAvailability Availability { get; set; }

	public string AvailabilityReason { get; set; } = string.Empty;
}
