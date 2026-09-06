namespace MacroDeck.Ui.Model.Surfaces;

/// <summary>
/// The <see cref="UiSurface.Attributes" /> keys a <see cref="UiSurfaceKinds.DeveloperPreview" /> surface
/// carries to say which registered preview scenario is being rendered.
/// </summary>
/// <remarks>
/// The scenario travels on the surface rather than in a session field for the same reason a widget's
/// configuration does: a provider outside the host cannot look the scenario up, and a new surface kind
/// must not cost the session vocabulary a new member every time one is added.
/// </remarks>
public static class UiDeveloperPreviewSurfaceAttributes
{
	/// <summary>Which registered scenario to build. A provider that does not recognise the id declines the
	/// session rather than serving an arbitrary one.</summary>
	public const string PreviewId = "previewId";

	/// <summary>Which primitive vocabulary the scenario authors its tree in, so a client can size the
	/// canvas before the first tree arrives. Advisory: the tree itself is what a renderer reads.</summary>
	public const string Profile = "profile";
}
