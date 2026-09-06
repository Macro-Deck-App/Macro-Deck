using System.Text.Json;
using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class GetWidgetTypesRequest
{
}

public class GetWidgetTypesResponse
{
	public bool Success { get; set; } = true;
	public TransportError? Error { get; set; }

	public List<WidgetTypeDto> Types { get; set; } = [];
}

/// <summary>One registered widget type: the entry the widget picker offers, and what a client needs to
/// decide how to configure a widget of it - see <c>UiConfigEntryPoints.WidgetConfig</c>.</summary>
public class WidgetTypeDto
{
	public string Id { get; set; } = string.Empty;

	/// <summary>The integration or plugin that provides it. Empty for a built-in type.</summary>
	public string ProviderId { get; set; } = string.Empty;

	public bool IsBuiltIn { get; set; }

	/// <summary>The type's name as the picker shows it. Localized rather than resolved here, so a language
	/// change is a client-side re-render rather than a refetch.</summary>
	public LocalizedText Name { get; set; }

	public LocalizedText Description { get; set; }

	/// <summary>The stored configuration a newly added widget of this type starts with. Always an object;
	/// a type that declares none reports <c>{}</c>.</summary>
	public JsonElement DefaultData { get; set; }

	/// <summary>Whether this type's provider can render its configuration as a Macro Deck UI tree. A
	/// client that cannot render a tree, or a type that does not support one, falls back to its own
	/// widget editor.</summary>
	public bool SupportsConfigUi { get; set; }

	/// <summary>The UI model major the tree would be built at, meaningful only when
	/// <see cref="SupportsConfigUi" /> is set.</summary>
	public int ConfigUiModelVersion { get; set; }
}

/// <summary>A provider registered or withdrew widget types. Carries the whole catalog, so a client
/// replaces rather than reconciles.</summary>
public sealed record WidgetTypeCatalogChangedEvent
{
	public IReadOnlyList<WidgetTypeDto> Types { get; init; } = [];
}
