using System.Text.Json.Serialization;
using MacroDeck.Sdk.Email;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

public class GetIntegrationEmailInboxesRequest
{
	public string IntegrationId { get; set; } = string.Empty;
}

public class GetIntegrationEmailInboxesResponse
{
	public bool Found { get; set; }

	public bool Available { get; set; }

	public List<IntegrationEmailInboxDto> Inboxes { get; set; } = [];
}

public class IntegrationEmailInboxDto
{
	public string Ref { get; set; } = string.Empty;

	public string DisplayName { get; set; } = string.Empty;

	public string? Address { get; set; }

	[JsonConverter(typeof(JsonStringEnumConverter))]
	public EmailInboxStatus Status { get; set; }

	public string? StatusMessage { get; set; }

	public int UnreadCount { get; set; }

	public int TotalCount { get; set; }

	public DateTimeOffset? SyncedAt { get; set; }

	public bool IsStale { get; set; }

	public List<IntegrationEmailMessageDto> Messages { get; set; } = [];
}

public class IntegrationEmailMessageDto
{
	public string Id { get; set; } = string.Empty;

	public string? SenderName { get; set; }

	public string? SenderAddress { get; set; }

	public string Subject { get; set; } = string.Empty;

	public string? Preview { get; set; }

	public DateTimeOffset ReceivedAt { get; set; }

	public bool IsRead { get; set; }
}
