using System.Text.Json.Serialization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Issues;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

public class ResolveIntegrationIssueResponse
{
	public bool Success { get; set; }

	public LocalizedText Message { get; set; }

	[JsonConverter(typeof(JsonStringEnumConverter))]
	public IssueResolutionFollowUp FollowUp { get; set; } = IssueResolutionFollowUp.None;

	public TransportError? Error { get; set; }
}
