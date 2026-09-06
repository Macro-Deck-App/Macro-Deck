using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Actions;

public class ActionDefinition
{
	public string Id { get; set; } = string.Empty;
	public string IntegrationId { get; set; } = string.Empty;

	public LocalizedText IntegrationName { get; set; }

	public LocalizedText Name { get; set; }

	public LocalizedText Description { get; set; }

	public List<ActionParameterDef> Parameters { get; set; } = new();
	public string? DescriptiveUiSchema { get; set; }

	/// <summary>Whether this action's configured instance can drive a button's state (issue #612).</summary>
	public bool IsStateProviderAction { get; set; }

	/// <summary>Whether this action's configured instance can drive a widget's icon (issue #425).
	/// Independent of <see cref="IsStateProviderAction" /> - an action may implement neither, either, or
	/// both capabilities.</summary>
	public bool IsIconProviderAction { get; set; }

	/// <summary>Whether this action can render its configuration as a Macro Deck UI tree instead of
	/// <see cref="Parameters" />. The declared parameter list stays authoritative either way - a client
	/// that cannot render a tree configures the action through it, unchanged.</summary>
	public bool SupportsConfigUi { get; set; }

	/// <summary>The UI model major the tree would be built at, meaningful only when
	/// <see cref="SupportsConfigUi" /> is set.</summary>
	public int ConfigUiModelVersion { get; set; }
}
