namespace MacroDeck.Sdk.FolderViews;

/// <summary>
/// Whether Macro Deck draws its own navigation chrome around a folder view. Open by construction: the
/// value is a string, not an enum, so a later version can name a third mode without every already-compiled
/// <c>switch</c> over it mis-handling the new member - the same reason
/// <see cref="Layouts.LayoutRegionKinds" /> is not an enum either. A reader that does not recognise a value
/// treats it as <see cref="Default" />, which is the safe answer: it shows the way back.
/// </summary>
public static class FolderViewNavigation
{
	/// <summary>Macro Deck renders its standard back button outside the provider's content area, wired to
	/// its own navigation stack. The provider cannot change where it goes.</summary>
	public const string Default = "default";

	/// <summary>The provider states it offers a self-contained way back and Macro Deck should draw none.
	/// A preference, not a guarantee: see <see cref="IFolderViewProvider" /> for when Macro Deck shows the
	/// button anyway.</summary>
	public const string Hidden = "hidden";

	/// <summary>The modes this package ships names for. Not exhaustive - see the type's summary.</summary>
	public static readonly IReadOnlyList<string> WellKnown = [Default, Hidden];
}
