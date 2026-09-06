namespace MacroDeck.Ui.Model.Surfaces;

/// <summary>
/// The <see cref="UiSurface.Attributes" /> keys a <see cref="UiSurfaceKinds.Config" /> surface carries
/// to say which configuration is being rendered.
/// </summary>
/// <remarks>
/// Configuration has several entry points that share a surface kind but nothing else: an integration's
/// config flow, one configured instance of an action, and one folder's selected folder view. Carrying the
/// discriminator as surface attributes rather than as new session fields is what let each be added without
/// touching the session contract, and is why a further entry point will not need one either.
/// </remarks>
public static class UiConfigSurfaceAttributes
{
	/// <summary>Which entry point opened the session - see <see cref="UiConfigEntryPoints" />.</summary>
	public const string EntryPoint = "entryPoint";

	/// <summary>The integration being configured, or the one owning the action being configured.</summary>
	public const string IntegrationId = "integrationId";

	/// <summary>The config flow session the tree belongs to. Only on
	/// <see cref="UiConfigEntryPoints.IntegrationConfig" />.</summary>
	public const string ConfigFlowSessionId = "configFlowSessionId";

	/// <summary>The action being configured. Only on <see cref="UiConfigEntryPoints.ActionConfig" />.</summary>
	public const string ActionId = "actionId";

	/// <summary>The values already stored for the action instance being configured, keyed by parameter
	/// name. Only on <see cref="UiConfigEntryPoints.ActionConfig" />.</summary>
	public const string Parameters = "parameters";

	/// <summary>
	/// The placeholder a <c>Secret</c>- or <c>Password</c>-typed parameter carries in
	/// <see cref="Parameters" /> instead of its real value - deliberately not the stored value, so a
	/// tree can render "configured" without the plugin process ever seeing what was configured.
	/// </summary>
	/// <remarks>
	/// Expanding a configuration surface is a UI interaction, not an explicit intent to reveal a stored
	/// secret - a user opening a card to check other fields must not be the thing that discloses a
	/// password to the plugin. The real value crosses only through the ordinary save path, when the
	/// user actually submits it.
	/// </remarks>
	public const string MaskedSecretValue = "$masked";

	/// <summary>The folder whose view is being configured. Only on
	/// <see cref="UiConfigEntryPoints.FolderViewConfig" />.</summary>
	public const string FolderId = "folderId";

	/// <summary>The qualified id of the folder view being configured, so a provider offering several
	/// declines one it does not configure rather than guessing. Only on
	/// <see cref="UiConfigEntryPoints.FolderViewConfig" />.</summary>
	public const string FolderViewId = "folderViewId";

	/// <summary>The folder view's currently stored configuration, as the JSON object it is stored as. Only
	/// on <see cref="UiConfigEntryPoints.FolderViewConfig" />; absent reads as empty.</summary>
	public const string FolderViewConfiguration = "folderViewConfiguration";

	/// <summary>The widget being configured, so two editors open on two widgets of the same type are two
	/// sessions. Only on <see cref="UiConfigEntryPoints.WidgetConfig" />.</summary>
	public const string WidgetId = "widgetId";

	/// <summary>The widget's type, so a provider serving several declines one it does not configure rather
	/// than guessing. Only on <see cref="UiConfigEntryPoints.WidgetConfig" />.</summary>
	public const string WidgetType = "widgetType";

	/// <summary>
	/// The widget's currently stored configuration, as the JSON object it is stored as. Only on
	/// <see cref="UiConfigEntryPoints.WidgetConfig" />; absent reads as empty.
	/// </summary>
	/// <remarks>
	/// The configuration travels on the surface rather than being looked up, for the reason
	/// <see cref="UiWidgetSurfaceAttributes.Data" /> already gives: a plugin cannot read the host's stored
	/// widgets. It is the draft being edited, not the saved record - the editor owns the transaction and
	/// writes it through the ordinary widget save path, never the tree.
	/// </remarks>
	public const string WidgetData = "widgetData";

	/// <summary>
	/// The widget's width and height in grid cells, so a control that previews the widget frames it the way
	/// the deck will. Only on <see cref="UiConfigEntryPoints.WidgetConfig" />; absent reads as one cell.
	/// </summary>
	/// <remarks>
	/// Geometry rather than configuration, which is why it travels beside <see cref="WidgetData" /> rather
	/// than inside it: where a widget sits and how large it is belongs to the deck, and a configuration tree
	/// neither reads it from the stored data nor writes it back.
	/// </remarks>
	public const string WidgetWidth = "widgetWidth";

	/// <inheritdoc cref="WidgetWidth" />
	public const string WidgetHeight = "widgetHeight";
}
