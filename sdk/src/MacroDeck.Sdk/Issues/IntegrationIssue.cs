using MacroDeck.Localization;

namespace MacroDeck.Sdk.Issues;

/// <summary>
/// A problem an integration is reporting that needs the user's attention (e.g. missing OS permission,
/// invalid credentials, disconnected service). Surfaced as a badge on the integration list and a box
/// in the integration detail view.
/// </summary>
public sealed class IntegrationIssue
{
	/// <summary>Stable id used to resolve the issue (see <see cref="IIntegrationIssueProvider.ResolveIssueAsync"/>).</summary>
	public required string Id { get; init; }

	public required LocalizedText Title { get; init; }

	public LocalizedText Description { get; init; }

	public IntegrationIssueSeverity Severity { get; init; } = IntegrationIssueSeverity.Warning;

	/// <summary>
	/// Label for the resolve button (e.g. "Grant permission", "Reconnect"). When null, the issue is
	/// informational and shows no action button.
	/// </summary>
	public LocalizedText ActionLabel { get; init; }

	/// <summary>
	/// A page with more help on the issue, e.g. a download page for a missing program. The issue box shows a
	/// button that opens it in the browser. Only an absolute <c>http</c> or <c>https</c> address is shown; the
	/// host drops any other value. Null shows no link, and a Macro Deck release that predates this member
	/// ignores it.
	/// </summary>
	public Uri? HelpUrl { get; init; }
}
