namespace MacroDeck.Ui.Model.Surfaces;

/// <summary>
/// The <see cref="UiSurface.Attributes" /> keys a <see cref="UiSurfaceKinds.Page" /> surface carries: one of
/// the pages an integration declared, rendered as a tab of that integration's page in the Macro Deck app.
/// </summary>
/// <remarks>
/// A page is <see cref="UiSessionModes.Exclusive" />: each opening of the tab is its own session, and only an
/// administrator's desktop app opens one, never a deck device.
/// </remarks>
public static class UiPageSurfaceAttributes
{
	/// <summary>The integration whose page is shown.</summary>
	public const string IntegrationId = "integrationId";

	/// <summary>Which of the integration's declared pages to open, by the id it declared. A provider that does
	/// not recognise it declines the session rather than guessing.</summary>
	public const string PageId = "pageId";

	/// <summary>The BCP 47 culture the person using Macro Deck reads it in, for example <c>de</c> or
	/// <c>pt-BR</c>, so a provider can format content it draws as text the way that person reads it. Absent
	/// when the host has not said.</summary>
	public const string Culture = "culture";
}
