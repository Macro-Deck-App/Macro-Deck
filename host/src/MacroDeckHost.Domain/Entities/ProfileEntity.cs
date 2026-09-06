using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Domain.Entities;

public class ProfileEntity : BaseEntity
{
	public required string Name { get; set; }

	public int Order { get; set; }

	public ProfileLayoutType LayoutType { get; set; } = ProfileLayoutType.Grid;

	public int DefaultRows { get; set; } = GridDefaults.Rows;

	public int DefaultColumns { get; set; } = GridDefaults.Columns;

	public string? DefaultBackgroundColor { get; set; }

	public int? DefaultWidgetSpacing { get; set; }

	public int? DefaultWidgetBorderRadius { get; set; }
}
