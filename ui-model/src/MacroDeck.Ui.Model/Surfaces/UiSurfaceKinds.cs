namespace MacroDeck.Ui.Model.Surfaces;

/// <summary>
/// The well-known <see cref="UiSurface.Kind" /> values this package ships names for today. Deliberately
/// exposes no <c>IsKnown</c> and the list is named <see cref="WellKnown" />, not <c>All</c>, so the
/// difference is visible at the call site: the moment an <c>IsKnown</c> exists, somebody gates on it
/// and an unrecognised surface kind becomes fatal, which would silently close a set the issue requires
/// to stay open for a future widget or dialog profile.
/// </summary>
public static class UiSurfaceKinds
{
	/// <summary>A configuration surface: a setup or settings flow.</summary>
	public const string Config = "config";

	/// <summary>A widget surface: a persistent, glanceable view.</summary>
	public const string Widget = "widget";

	/// <summary>A dialog surface: a transient, modal interaction.</summary>
	public const string Dialog = "dialog";

	/// <summary>A preview surface: a read-only rendering, for example in an editor.</summary>
	public const string Preview = "preview";

	/// <summary>A folder surface: the whole of one folder, rendered by a folder view provider instead of
	/// the built-in widget grid.</summary>
	public const string Folder = "folder";

	/// <summary>A developer preview surface: one registered preview scenario, rendered in Developer Tools.
	/// Distinct from <see cref="Preview" />, which renders an unsaved draft of a real widget - a provider
	/// that serves a production surface must never be reached by a developer preview.</summary>
	public const string DeveloperPreview = "developer-preview";

	/// <summary>The kinds this package ships names for. Not exhaustive - see the type's remarks.</summary>
	public static readonly IReadOnlyList<string> WellKnown =
		[Config, Widget, Dialog, Preview, Folder, DeveloperPreview];
}
