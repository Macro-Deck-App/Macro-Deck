using System.Text.Json.Serialization;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class Widget
{
	public string Id { get; set; } = string.Empty;

	public string Type { get; set; } = WidgetTypeIds.ActionButton;

	public int PositionX { get; set; }
	public int PositionY { get; set; }
	public int Width { get; set; }
	public int Height { get; set; }
	public string? Data { get; set; }

	public bool IsPinned { get; set; }

	[JsonConverter(typeof(JsonStringEnumConverter))]
	public PinScope PinScope { get; set; }
}
