using MacroDeck.Localization;

namespace MacroDeck.Sdk.Ui;

/// <summary>
/// One page an <see cref="IUiProvider" /> adds as a tab to its integration's page in the Macro Deck app, for
/// example a library of items the integration manages. Macro Deck opens a <c>page</c> surface for it when an
/// administrator selects the tab; see <c>UiPageSurfaceAttributes</c> for what that surface carries.
/// </summary>
/// <remarks>
/// A declaration, read before anything is initialized and possibly again later, exactly like
/// <see cref="IUiProvider.Surfaces" />, so it must not depend on a live connection or completed
/// configuration. A provider that declares pages must also declare a <c>page</c> surface in
/// <see cref="IUiProvider.Surfaces" />; Macro Deck ignores the pages of a provider that does not.
/// </remarks>
public sealed record UiPageDeclaration
{
	/// <summary>Identifies the page within the integration and reaches the provider as the surface's
	/// <c>pageId</c>. An ASCII letter or digit followed by up to 63 more letters, digits, hyphens or
	/// underscores; Macro Deck ignores a page whose id does not have that form or repeats an earlier
	/// one.</summary>
	public required string Id { get; init; }

	/// <summary>The tab's label, in the reader's own language.</summary>
	public required LocalizedText Title { get; init; }

	/// <summary>The name of a built-in Macro Deck icon shown beside <see cref="Title" />, or <c>null</c> for
	/// none. A name the client does not know shows no icon.</summary>
	public string? IconName { get; init; }
}
