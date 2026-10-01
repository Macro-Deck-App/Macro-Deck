using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.VideoStreams;

public sealed record GetVideoStreamsRequest;

public sealed record GetVideoStreamsResponse
{
	public IReadOnlyList<VideoStreamProviderItem> Providers { get; init; } = [];
}

public sealed record VideoStreamProviderItem
{
	public string Id { get; init; } = string.Empty;

	public LocalizedText Name { get; init; }

	public LocalizedText? Description { get; init; }

	public IReadOnlyList<VideoStreamItem> Streams { get; init; } = [];
}

public sealed record VideoStreamItem
{
	public string Id { get; init; } = string.Empty;

	public LocalizedText Name { get; init; }

	public LocalizedText? Description { get; init; }

	public int? Width { get; init; }

	public int? Height { get; init; }

	public bool HasAudio { get; init; }

	public string State { get; init; } = string.Empty;
}

public sealed record OpenVideoStreamRequest
{
	public string ProviderId { get; init; } = string.Empty;

	public string StreamId { get; init; } = string.Empty;

	public IReadOnlyList<string> AcceptedTransports { get; init; } = [];
}

public sealed record OpenVideoStreamResponse
{
	public string SessionId { get; init; } = string.Empty;

	public long Revision { get; init; }

	public string State { get; init; } = string.Empty;
}

public sealed record KeepAliveVideoStreamRequest
{
	public string SessionId { get; init; } = string.Empty;
}

public sealed record SuspendVideoStreamRequest
{
	public string SessionId { get; init; } = string.Empty;
}

public sealed record ResumeVideoStreamRequest
{
	public string SessionId { get; init; } = string.Empty;
}

public sealed record CloseVideoStreamRequest
{
	public string SessionId { get; init; } = string.Empty;
}

public sealed record VideoStreamDescriptionMessage
{
	public string Transport { get; init; } = string.Empty;

	public string? Url { get; init; }
}

public sealed record VideoStreamCatalogChangedEvent;

public sealed record VideoStreamSessionChangedEvent
{
	public string SessionId { get; init; } = string.Empty;

	public long Revision { get; init; }

	public string State { get; init; } = string.Empty;

	public VideoStreamDescriptionMessage? Description { get; init; }

	public string Reason { get; init; } = string.Empty;

	public LocalizedText? Message { get; init; }
}

public sealed record VideoStreamSessionClosedEvent
{
	public string SessionId { get; init; } = string.Empty;

	public string Reason { get; init; } = string.Empty;

	public string? Error { get; init; }

	public LocalizedText? Message { get; init; }
}
