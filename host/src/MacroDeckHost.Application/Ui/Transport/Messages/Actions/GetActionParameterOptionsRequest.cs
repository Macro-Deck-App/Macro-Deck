using System.Text.Json;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Actions;

public class GetActionParameterOptionsRequest
{
	public string IntegrationId { get; set; } = string.Empty;

	public string ActionId { get; set; } = string.Empty;

	public string? EventId { get; set; }

	/// <summary>
	/// Resolves a host-backed options source directly, for a surface that renders parameter controls
	/// without an owning action or event - a config-flow step's fields. Ignored when an action or event
	/// addresses the parameter.
	/// </summary>
	public string? OptionsSourceId { get; set; }

	/// <summary>
	/// Widget type names to restrict a widget-target choice to, for the <see cref="OptionsSourceId" />
	/// form where there is no parameter declaration on this side to read them from.
	/// </summary>
	public IReadOnlyList<string>? WidgetTypes { get; set; }

	/// <summary>
	/// Which of an event's two parameter lists <see cref="ParameterName" /> names -
	/// <see cref="EventParameterKinds" />. An event may declare the same name in both with different
	/// metadata, so this cannot be inferred by searching one list and falling back to the other.
	/// Ignored unless <see cref="EventId" /> is set; absent means a configuration parameter.
	/// </summary>
	public string? EventParameterKind { get; set; }

	public string ParameterName { get; set; } = string.Empty;

	public string? Filter { get; set; }

	public Dictionary<string, JsonElement>? CurrentParameters { get; set; }
}
