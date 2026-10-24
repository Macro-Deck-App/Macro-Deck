using MacroDeck.Ui.Dsl;

namespace MacroDeck.Ui.Config;

/// <summary>
/// A page laid out as a rail beside a detail area, the way Macro Deck draws its own library pages: a titled rail
/// listing what the page manages, with actions such as New in the rail's header, and the selected item's detail
/// beside it. A reader stacks the two on a narrow screen.
///
/// <para>
/// <b>On the wire the node always has exactly three children, in this order:</b> a <c>stack</c> holding
/// <see cref="RailActions" />, a <c>stack</c> holding <see cref="Rail" />, and a <c>stack</c> holding
/// <see cref="Detail" />, each present even when its region is empty. Their keys are <c>rail-actions</c>,
/// <c>rail</c> and <c>detail</c> under the split's own id. A region is a position rather than a property because
/// it holds nodes: a patch inserts into, removes from and moves within a region exactly as within any stack, and
/// a reader finds each region at a fixed index without knowing anything new about stacks. The region stacks
/// carry no properties of their own, and do not open an input scope, so an input in the detail keeps its bare
/// key.
/// </para>
///
/// <para>
/// A reader that predates the type declines it and draws the node's <see cref="UiElement.Fallback" />. Nothing
/// is substituted automatically, so give a split a fallback where an older reader matters.
/// </para>
/// </summary>
public sealed record UiSplit : UiElement
{
	/// <summary>The rail's caption, such as the name of what the page manages.</summary>
	public UiText RailTitle { get; init; }

	/// <summary>Controls drawn in the rail's header beside <see cref="RailTitle" />, typically an icon
	/// <see cref="UiConfigButton" /> that adds an item, or a <see cref="UiConfigMenu" />.</summary>
	public IReadOnlyList<UiElement> RailActions
	{
		get;
		init => field = UiElementLists.DropNullElements(value);
	} = [];

	/// <summary>The rail's content, typically a <see cref="UiConfigList" />.</summary>
	public IReadOnlyList<UiElement> Rail
	{
		get;
		init => field = UiElementLists.DropNullElements(value);
	} = [];

	/// <summary>The detail area's content, drawn on a card: the selected item, or a <see cref="UiEmptyState" />
	/// while nothing is selected.</summary>
	public IReadOnlyList<UiElement> Detail
	{
		get;
		init => field = UiElementLists.DropNullElements(value);
	} = [];

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Split;

	/// <summary>The key of the region stack holding <see cref="RailActions" />.</summary>
	public const string RailActionsKey = "rail-actions";

	/// <summary>The key of the region stack holding <see cref="Rail" />.</summary>
	public const string RailKey = "rail";

	/// <summary>The key of the region stack holding <see cref="Detail" />.</summary>
	public const string DetailKey = "detail";

	internal IReadOnlyList<UiElement> Regions =>
	[
		new UiConfigStack { Key = RailActionsKey, Children = RailActions },
		new UiConfigStack { Key = RailKey, Children = Rail },
		new UiConfigStack { Key = DetailKey, Children = Detail }
	];

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.RailTitle, RailTitle.Value);
	}
}

/// <summary>
/// A list of selectable rows. Its children are <see cref="UiConfigListItem" />s, drawn in order. A list contributes no
/// key and no value: what is selected is the provider's own state, reported through each item's
/// <see cref="UiConfigListItem.Selected" />. A reader that predates the type declines it and draws the node's
/// fallback.
/// </summary>
public sealed record UiConfigList : UiContainer
{
	/// <summary>The list's accessible name.</summary>
	public UiText Label { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.List;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Label, Label.Value);
	}
}

/// <summary>
/// One row of a <see cref="UiConfigList" />: a <see cref="Label" />, a muted <see cref="Description" /> line, an
/// optional leading <see cref="Icon" />, a <see cref="Badge" />, and a status dot coloured by
/// <see cref="Severity" />. Choosing the row raises <c>activate</c>; the handler selects it by setting
/// <see cref="Selected" />. Children are drawn trailing the row, typically a <see cref="UiConfigMenu" /> with the
/// row's own actions, and choosing one of them does not activate the row.
///
/// <para>
/// <b>Selection is the session's, not the item's.</b> A page surface is exclusive, so each client that opens a
/// page has its own session and its own selection; in a shared surface every attached client would see the same
/// row selected. A reader that predates the type declines it and draws the node's fallback.
/// </para>
/// </summary>
public sealed record UiConfigListItem : UiContainer
{
	/// <summary>The row's text, and its accessible name.</summary>
	public UiText Label { get; init; }

	/// <summary>A muted second line, such as the item's state or last result. Absent draws one line.</summary>
	public UiText Description { get; init; }

	/// <summary>An icon name drawn ahead of the text. Absent draws none.</summary>
	public UiValue<string> Icon { get; init; }

	/// <summary>A short tag drawn after the label, such as a count. Absent draws none.</summary>
	public UiText Badge { get; init; }

	/// <summary>Colours the row's status dot with the severity vocabulary <see cref="UiBanner.Severity" /> uses:
	/// <c>info</c>, <c>success</c>, <c>warning</c> or <c>error</c>. Absent draws no dot; a value a reader does not
	/// know draws a neutral one.</summary>
	public UiValue<string> Severity { get; init; }

	/// <summary>Whether this is the selected row. Absent means not selected. At most one row of a list should be
	/// selected; a reader marks every row that says so.</summary>
	public UiValue<bool> Selected { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.ListItem;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Label, Label.Value);
		properties.Set(UiConfigProperties.Description, Description.Value);
		properties.Set(UiConfigProperties.Icon, Icon);
		properties.Set(UiConfigProperties.Badge, Badge.Value);
		properties.Set(UiConfigProperties.Severity, Severity);
		properties.Set(UiConfigProperties.Selected, Selected);
	}
}

/// <summary>
/// What a region shows while it has nothing to show, such as a detail area with nothing selected: an
/// <see cref="Icon" />, a <see cref="Title" /> and a line of <see cref="Text" />. Children are drawn below the
/// text, typically a <see cref="UiConfigButton" /> that creates the first item. A reader that predates the type
/// declines it and draws the node's fallback.
/// </summary>
public sealed record UiEmptyState : UiContainer
{
	/// <summary>An icon name drawn above the title. Absent draws none.</summary>
	public UiValue<string> Icon { get; init; }

	/// <summary>The short headline.</summary>
	public UiText Title { get; init; }

	/// <summary>The sentence below the headline.</summary>
	public UiText Text { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.EmptyState;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Icon, Icon);
		properties.Set(UiConfigProperties.Title, Title.Value);
		properties.Set(UiConfigProperties.Text, Text.Value);
	}
}
