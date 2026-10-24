namespace MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

public sealed record OpenPageUiSessionRequest
{
	public string IntegrationId { get; init; } = string.Empty;

	public string PageId { get; init; } = string.Empty;
}

public sealed record OpenPageUiSessionResponse
{
	public required bool Accepted { get; init; }

	public required string SessionId { get; init; }

	public string? Code { get; init; }

	public string? Message { get; init; }
}
