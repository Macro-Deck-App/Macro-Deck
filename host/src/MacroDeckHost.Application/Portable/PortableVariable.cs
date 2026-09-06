using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Portable;

// Deliberately carries no Scope, Classification, OwnerIntegrationId, DefinitionId, Presentation,
// UpdateMode or Id: a hand-edited archive cannot express any of them, so everything restored from a
// PortableVariable is User-classified widget-scoped by construction of the restore path alone.
public sealed class PortableVariable
{
	// The SOURCE widget id. Only ever used as a map key to correlate with PortableWidget.SourceId /
	// the archive's own widget ids on import - never written back as a live variable's ScopeRefId.
	public Guid WidgetId { get; set; }

	public string Name { get; set; } = string.Empty;

	public VariableType Type { get; set; }

	public string Value { get; set; } = string.Empty;

	public int? DecimalPlaces { get; set; }
}
