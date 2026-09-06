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
}
