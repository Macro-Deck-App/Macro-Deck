namespace MacroDeckHost.Domain.Entities;

public class FolderEntity : BaseEntity
{
	public required string Name { get; set; }

	public Guid ProfileId { get; set; }

	public Guid? ParentId { get; set; }

	public required int Order { get; set; }

	public int? Rows { get; set; }

	public int? Columns { get; set; }

	public string? BackgroundColor { get; set; }

	public int? WidgetSpacing { get; set; }

	public int? WidgetBorderRadius { get; set; }

	public bool IsDefault { get; set; }

	/// <summary>
	/// Which folder view renders this folder. Null or empty is the built-in widget grid, which is what
	/// every folder stored before folder views existed reads as - see
	/// <c>BuiltInFolderViews.IsWidgetGrid</c>. Any other value is a provider's qualified view id, kept
	/// verbatim even while nothing provides it, so uninstalling an integration never destroys a folder's
	/// setup.
	/// </summary>
	public string? ViewId { get; set; }

	/// <summary>The view's configuration, as the JSON object text it is stored as - opaque to Macro Deck,
	/// exactly like a widget's <c>Data</c>. Retained alongside <see cref="ViewId" />.</summary>
	public string? ViewConfiguration { get; set; }

	public List<WidgetEntity> Widgets { get; set; } = [];

	public List<FolderFocusRule> FocusRules { get; set; } = [];
}
