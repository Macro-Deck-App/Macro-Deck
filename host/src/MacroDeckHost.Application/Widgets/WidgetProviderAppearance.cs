using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Widgets;

public sealed class WidgetProviderAppearance
{
	private readonly IWidgetTypeRegistry _widgetTypes;
	private readonly IWidgetDataSchemaProvider _schemas;
	private readonly WidgetAppearanceSchemaProbe _schemaProbe;

	public WidgetProviderAppearance(
		IWidgetTypeRegistry widgetTypes,
		IWidgetDataSchemaProvider schemas,
		WidgetAppearanceSchemaProbe schemaProbe)
	{
		_widgetTypes = widgetTypes;
		_schemas = schemas;
		_schemaProbe = schemaProbe;
	}

	public List<WidgetAppearanceProperty>? Supported(WidgetEntity widget)
	{
		if (!_widgetTypes.TryResolve(widget.Type, out var entry) || entry.IsBuiltIn)
		{
			return null;
		}

		var supported = new List<WidgetAppearanceProperty>
		{
			WidgetAppearanceProperty.Border, WidgetAppearanceProperty.BorderColor
		};

		var declared = entry.Descriptor.AppearanceProperties ?? [];
		var hasSchema = _schemas.TryGet(widget.Type, out var schema);
		foreach (var property in WidgetAppearanceJson.ProviderOptionalProperties)
		{
			if (declared.Contains(property)
				&& (!hasSchema || _schemaProbe.Accepts(widget.Type, schema!, widget.Data ?? string.Empty, property)))
			{
				supported.Add(property);
			}
		}

		return supported;
	}
}
