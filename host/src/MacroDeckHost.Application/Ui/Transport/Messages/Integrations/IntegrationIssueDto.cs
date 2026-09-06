using MacroDeck.Localization;
using MacroDeck.Sdk.Issues;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

public class IntegrationIssueDto
{
	public string Id { get; set; } = string.Empty;

	public LocalizedText Title { get; set; }

	public LocalizedText Description { get; set; }

	public string Severity { get; set; } = "warning";

	public LocalizedText ActionLabel { get; set; }

	public static IntegrationIssueDto From(IntegrationIssue issue) => new()
	{
		Id = issue.Id,
		Title = issue.Title,
		Description = issue.Description,
		Severity = SeverityName(issue.Severity),
		ActionLabel = issue.ActionLabel
	};

	public static string SeverityName(IntegrationIssueSeverity severity) => severity switch
	{
		IntegrationIssueSeverity.Error => "error",
		IntegrationIssueSeverity.Warning => "warning",
		_ => "info"
	};

	public static string? MaxSeverityName(IReadOnlyList<IntegrationIssue> issues)
		=> issues.Count == 0 ? null : SeverityName(issues.Max(i => i.Severity));
}
