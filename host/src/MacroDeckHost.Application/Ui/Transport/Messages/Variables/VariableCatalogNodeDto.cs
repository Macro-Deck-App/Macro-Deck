using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Variables;

/// <summary>One resource of a provider's variable catalog, as sent to the client.</summary>
public class VariableCatalogNodeDto
{
	/// <summary>The provider-local resource id - the part after <c>::</c>.</summary>
	public string Id { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public LocalizedText DisplayName { get; set; }

	public LocalizedText Description { get; set; }

	/// <summary>Whether the browser should show a disclosure control and let the user open this node.</summary>
	public bool HasChildren { get; set; }

	/// <summary>The value type this resource would bind as, or <c>null</c> for a grouping node that
	/// carries no value of its own and therefore cannot be bound.</summary>
	public string? Type { get; set; }

	public string? Icon { get; set; }

	/// <summary>
	/// The variable name the host would derive if the user binds this resource without naming it - the
	/// provider's own suggestion, before canonicalization and collision handling. The bind dialog
	/// prefills it so the name the user is offered is the one binding would actually produce.
	/// </summary>
	public string? SuggestedName { get; set; }

	/// <summary>The id of the variable this resource is already bound to, when it is bound.</summary>
	public string? BoundVariableId { get; set; }

	public int? DecimalPlaces { get; set; }

	/// <summary>Whether the owner accepts writes for this resource. Declared, not probed, so a browser
	/// offering only writable leaves can refuse one before it is ever materialized.</summary>
	public bool CanWrite { get; set; }
}
