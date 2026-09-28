using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Capabilities.VideoStreamProvider;

namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>
/// Arguments for the <see cref="HostApis.VideoStreams" /> api's <c>streams-changed</c> operation. The
/// host answers by re-reading the provider's streams; <c>providers-changed</c> takes no arguments.
/// </summary>
public sealed record VideoStreamsStreamsChangedArguments
{
	public required string ProviderId { get; init; }
}

/// <summary>
/// Arguments for the <c>session-update</c> operation. The host only accepts it for a session it opened
/// on one of the calling plugin's own providers.
/// </summary>
public sealed record VideoStreamsSessionUpdateArguments
{
	public required string SessionId { get; init; }

	/// <summary>One of the SDK's <c>VideoStreamSessionState</c> member names. A reader maps a value it does
	/// not know to <c>Reconnecting</c>.</summary>
	public required string State { get; init; }

	/// <summary>A replacement description, or absent when the current one stays valid.</summary>
	public VideoStreamSessionDescriptionDto? Description { get; init; }

	/// <summary>One of the SDK's <c>VideoStreamSessionReason</c> member names. A reader maps a value it
	/// does not know to <c>None</c>.</summary>
	public string Reason { get; init; } = "None";

	public LocalizedText? Message { get; init; }
}

/// <summary>Arguments for the <c>session-signal</c> operation: a signal from the provider to the consumer.</summary>
public sealed record VideoStreamsSessionSignalArguments
{
	public required string SessionId { get; init; }

	public required VideoStreamSignalDto Signal { get; init; }
}

/// <summary>Arguments for the <c>session-close</c> operation: the provider ended the session itself.</summary>
public sealed record VideoStreamsSessionCloseArguments
{
	public required string SessionId { get; init; }

	/// <summary>One of the SDK's <c>VideoStreamSessionReason</c> member names. A reader maps a value it
	/// does not know to <c>None</c>.</summary>
	public string Reason { get; init; } = "ProviderClosed";

	public LocalizedText? Message { get; init; }
}
