namespace MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

public class IntegrationIssuesChangedEvent
{
	public string IntegrationId { get; set; } = string.Empty;

	public List<IntegrationIssueDto> Issues { get; set; } = [];

	public int IssueCount { get; set; }

	public string? Severity { get; set; }
}
