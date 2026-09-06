using MacroDeck.Plugin.Protocol.Capabilities.WidgetTypeProvider;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Plugin.Hosting.Capabilities.WidgetTypeProvider;

/// <summary>Maps between the SDK's widget type descriptor and its wire shape.</summary>
internal static class WidgetTypeDescriptorMapper
{
	public static WidgetTypeDescriptorDto ToDto(WidgetTypeDescriptor widgetType)
	{
		ArgumentNullException.ThrowIfNull(widgetType);

		return new WidgetTypeDescriptorDto
		{
			Id = widgetType.Id,
			Name = widgetType.Name,
			Description = widgetType.Description,
			DefaultData = widgetType.DefaultData,
			DataSchema = widgetType.DataSchema,
			HasConfiguration = widgetType.HasConfiguration,
			Metadata = widgetType.Metadata
		};
	}
}
