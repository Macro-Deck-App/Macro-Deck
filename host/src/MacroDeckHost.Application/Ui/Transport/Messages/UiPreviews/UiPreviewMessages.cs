namespace MacroDeckHost.Application.Ui.Transport.Messages.UiPreviews;

public sealed record ListUiPreviewsRequest;

/// <summary>One discovered preview scenario, as Developer Tools lists it.</summary>
public sealed record UiPreviewEntry
{
	public required string Id { get; init; }

	public required string View { get; init; }

	public required string Scenario { get; init; }

	public required string Profile { get; init; }

	/// <summary>The plugin that declares it, or empty for a first-party preview.</summary>
	public required string OwnerId { get; init; }
}

public sealed record UiPreviewDiagnosticEntry
{
	public required string Member { get; init; }

	public required string Reason { get; init; }
}

public sealed record ListUiPreviewsResponse
{
	public IReadOnlyList<UiPreviewEntry> Previews { get; init; } = [];

	public IReadOnlyList<UiPreviewDiagnosticEntry> Diagnostics { get; init; } = [];
}

public sealed record OpenUiPreviewSessionRequest
{
	public string PreviewId { get; init; } = string.Empty;
}

public sealed record OpenUiPreviewSessionResponse
{
	public required bool Accepted { get; init; }

	public required string SessionId { get; init; }

	public string? Code { get; init; }

	public string? Message { get; init; }
}
