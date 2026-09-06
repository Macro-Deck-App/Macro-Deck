using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Persistence.Profiles;

public sealed class ProfileFolder
{
	public Guid Id { get; set; }

	public string Name { get; set; } = string.Empty;

	public Guid? ParentId { get; set; }

	public int Order { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int? Rows { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int? Columns { get; set; }

	public string? BackgroundColor { get; set; }

	public int? WidgetSpacing { get; set; }

	public int? WidgetBorderRadius { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool IsDefault { get; set; }

	/// <summary>Absent is the built-in widget grid - which is what every folder written before folder
	/// views existed reads as, so no stored profile needs migrating.</summary>
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ViewId { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ViewConfiguration { get; set; }

	public DateTime CreatedAt { get; set; }

	public List<ProfileWidget> Widgets { get; set; } = [];

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<ProfileFolderFocusRule>? FocusRules { get; set; }
}
