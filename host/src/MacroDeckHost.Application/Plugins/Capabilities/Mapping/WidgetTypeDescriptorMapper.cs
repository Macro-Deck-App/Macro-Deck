using MacroDeck.Plugin.Protocol.Capabilities.WidgetTypeProvider;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Application.Plugins.Capabilities.Mapping;

/// <summary>Turns the wire shape of a widget type registration into the SDK descriptor the host works
/// with.</summary>
public static class WidgetTypeDescriptorMapper
{
	public static WidgetTypeDescriptor ToDescriptor(WidgetTypeDescriptorDto dto)
	{
		ArgumentNullException.ThrowIfNull(dto);

		return new WidgetTypeDescriptor(dto.Id,
			dto.Name,
			dto.Description,
			dto.DefaultData,
			dto.DataSchema,
			dto.HasConfiguration,
			dto.Metadata);
	}
}
