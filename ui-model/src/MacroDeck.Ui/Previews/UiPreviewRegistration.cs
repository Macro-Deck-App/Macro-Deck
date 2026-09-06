using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeck.Ui.Previews;

/// <summary>A discovered scenario together with the factory that builds it.</summary>
public sealed class UiPreviewRegistration
{
	internal UiPreviewRegistration(UiPreviewDeclaration declaration, Func<UiSurface, UiPreviewInstance> create)
	{
		Declaration = declaration;
		Create = create;
	}

	/// <summary>What Developer Tools lists.</summary>
	public UiPreviewDeclaration Declaration { get; }

	/// <summary>
	/// Builds a fresh rendering for the surface the session is opened on. Called once per session, so
	/// every open and every refresh gets scenario state no previous rendering ever touched.
	/// </summary>
	public Func<UiSurface, UiPreviewInstance> Create { get; }
}
