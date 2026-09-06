using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Domain.Entities;

public class WidgetEntity : BaseEntity
{
	public Guid FolderId { get; set; }

	/// <summary>The widget's type id - see <see cref="WidgetTypeIds" />. An open string: an id no
	/// provider is registered for is stored and round-tripped unchanged.</summary>
	public string Type { get; set; } = WidgetTypeIds.ActionButton;

	public int PositionX { get; set; }

	public int PositionY { get; set; }

	public int Width { get; set; } = 1;

	public int Height { get; set; } = 1;

	public string? Data { get; set; }

	public bool IsPinned { get; set; }

	public PinScope PinScope { get; set; }
}
