namespace MacroDeck.Ui.Model.Surfaces;

/// <summary>
/// The well-known <see cref="UiConfigSurfaceAttributes.EntryPoint" /> values. Open like every other
/// surface vocabulary: a provider that does not recognise the entry point declines the session rather
/// than guessing what it was asked to render.
/// </summary>
public static class UiConfigEntryPoints
{
	/// <summary>An integration's config flow.</summary>
	public const string IntegrationConfig = "integration-config";

	/// <summary>One configured instance of an action.</summary>
	public const string ActionConfig = "action-config";

	/// <summary>One folder's selected folder view, configured from the folder's own settings.</summary>
	public const string FolderViewConfig = "folder-view-config";

	/// <summary>One placed widget, configured from the widget editor.</summary>
	public const string WidgetConfig = "widget-config";

	/// <summary>The entry points this package ships names for. Not exhaustive - see the type's summary.</summary>
	public static readonly IReadOnlyList<string> WellKnown =
		[IntegrationConfig, ActionConfig, FolderViewConfig, WidgetConfig];
}
