using System.Text.Json.Serialization;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Persistence.Profiles;

public sealed class ProfileFile
{
	public Guid Id { get; set; }

	public string Name { get; set; } = string.Empty;

	public int Order { get; set; }

	public ProfileLayoutType LayoutType { get; set; } = ProfileLayoutType.Grid;

	public int DefaultRows { get; set; } = GridDefaults.Rows;

	public int DefaultColumns { get; set; } = GridDefaults.Columns;

	public string? DefaultBackgroundColor { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int? DefaultWidgetSpacing { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int? DefaultWidgetBorderRadius { get; set; }

	public List<ProfileFolder> Folders { get; set; } = [];
}
