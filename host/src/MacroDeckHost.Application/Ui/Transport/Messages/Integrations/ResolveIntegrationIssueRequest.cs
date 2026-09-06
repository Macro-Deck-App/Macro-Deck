namespace MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

public class ResolveIntegrationIssueRequest
{
	public string IntegrationId { get; set; } = string.Empty;

	public string IssueId { get; set; } = string.Empty;
}
