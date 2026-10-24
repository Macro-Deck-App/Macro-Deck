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

	public string? HelpUrl { get; set; }

	public static IntegrationIssueDto From(IntegrationIssue issue)
	{
		return new IntegrationIssueDto
		{
			Id = issue.Id,
			Title = issue.Title,
			Description = issue.Description,
			Severity = SeverityName(issue.Severity),
			ActionLabel = issue.ActionLabel,
			HelpUrl = WebAddress(issue.HelpUrl)
		};
	}

	// The desktop opens this address in the system browser, so anything but a web page is dropped.
	private static string? WebAddress(Uri? url)
	{
		return url is { IsAbsoluteUri: true } && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)
			? url.AbsoluteUri
			: null;
	}

	public static string SeverityName(IntegrationIssueSeverity severity)
	{
		return severity switch
		{
			IntegrationIssueSeverity.Error => "error",
			IntegrationIssueSeverity.Warning => "warning",
			_ => "info"
		};
	}

	public static string? MaxSeverityName(IReadOnlyList<IntegrationIssue> issues)
	{
		return issues.Count == 0 ? null : SeverityName(issues.Max(i => i.Severity));
	}
}
