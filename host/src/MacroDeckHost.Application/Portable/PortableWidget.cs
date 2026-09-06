using System.Text.Json.Serialization;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Application.Portable;

public sealed class PortableWidget
{
	public string Type { get; set; } = WidgetTypeIds.ActionButton;

	public int PositionX { get; set; }

	public int PositionY { get; set; }

	public int Width { get; set; } = 1;

	public int Height { get; set; } = 1;

	public string? Data { get; set; }

	// The source widget id, so a widgets archive can correlate its widgets with their PortableVariables.
	// Deliberately NOT fed into PortableGuidRemapper: folder/profile archives do rewrite widget-id
	// references inside Data, but a widgets archive deliberately does not, unchanged from before this
	// field existed.
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public Guid? SourceId { get; set; }
}
