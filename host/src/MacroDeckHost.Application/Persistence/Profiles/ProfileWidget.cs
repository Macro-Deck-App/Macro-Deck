using System.Text.Json.Serialization;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Application.Persistence.Profiles;

public sealed class ProfileWidget
{
	public Guid Id { get; set; }

	public string Type { get; set; } = WidgetTypeIds.ActionButton;

	public int PositionX { get; set; }

	public int PositionY { get; set; }

	public int Width { get; set; } = 1;

	public int Height { get; set; } = 1;

	public string? Data { get; set; }

	public bool IsPinned { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public PinScope PinScope { get; set; }
}
