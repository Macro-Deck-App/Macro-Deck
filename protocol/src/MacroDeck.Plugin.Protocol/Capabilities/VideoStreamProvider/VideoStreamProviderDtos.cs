using MacroDeck.Localization;

namespace MacroDeck.Plugin.Protocol.Capabilities.VideoStreamProvider;

/// <summary>
/// One provider in the result of the <c>video-stream-provider</c> capability's <c>describe</c> operation.
/// </summary>
public sealed record VideoStreamProviderDto
{
	/// <summary>The provider-local id, unique within the plugin. The host qualifies it with the plugin.</summary>
	public required string Id { get; init; }

	public required LocalizedText Name { get; init; }

	public LocalizedText? Description { get; init; }

	/// <summary>
	/// Fresh for every registration. A host that sees it change, or vanish, for the same <see cref="Id" />
	/// treats every session it opened on the earlier registration as closed.
	/// </summary>
	public required string RegistrationId { get; init; }
}

/// <summary>Result of the <c>describe</c> operation: every provider the plugin currently has registered.</summary>
public sealed record VideoStreamProviderDescribePayload
{
	public IReadOnlyList<VideoStreamProviderDto> Providers { get; init; } = [];
}

/// <summary>
/// Mirrors the SDK's <c>VideoStreamDescriptor</c>. <see cref="State" /> is a string, not the SDK enum -
/// see <c>ActionParameterDto</c>'s remarks.
/// </summary>
public sealed record VideoStreamDescriptorDto
{
	public required string Id { get; init; }

	public required LocalizedText Name { get; init; }

	public LocalizedText? Description { get; init; }

	public int? Width { get; init; }

	public int? Height { get; init; }

	public bool HasAudio { get; init; }

	/// <summary>One of the SDK's <c>VideoStreamState</c> member names. A reader maps a value it does not
	/// know to <c>Unavailable</c>.</summary>
	public string State { get; init; } = "Connected";

	public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>Arguments for the <c>streams</c> operation.</summary>
public sealed record VideoStreamProviderStreamsArguments
{
	public required string ProviderId { get; init; }
}

/// <summary>Result of the <c>streams</c> operation.</summary>
public sealed record VideoStreamProviderStreamsResult
{
	public IReadOnlyList<VideoStreamDescriptorDto> Streams { get; init; } = [];
}

/// <summary>Mirrors the SDK's <c>VideoStreamSessionDescription</c>. Opaque to the host.</summary>
public sealed record VideoStreamSessionDescriptionDto
{
	/// <summary>A lowercase transport token, for example <c>webrtc</c> or <c>hls</c>.</summary>
	public required string Transport { get; init; }

	public string? Url { get; init; }

	public IReadOnlyDictionary<string, string>? Parameters { get; init; }

	public string? Payload { get; init; }

	public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>Mirrors the SDK's <c>VideoStreamSignal</c>. Opaque to the host.</summary>
public sealed record VideoStreamSignalDto
{
	public required string Type { get; init; }

	public required string Payload { get; init; }
}

/// <summary>Mirrors the SDK's <c>VideoStreamConsumer</c>.</summary>
public sealed record VideoStreamConsumerDto
{
	public string? DeviceId { get; init; }

	/// <summary>The absolute host address the consumer used to reach Macro Deck, when known.</summary>
	public string? HostAddress { get; init; }

	/// <summary>One of the SDK's <c>VideoStreamConnectionKind</c> member names. A reader maps a value it
	/// does not know to <c>Network</c>.</summary>
	public string ConnectionKind { get; init; } = "Network";
}

/// <summary>Arguments for the <c>session.open</c> operation.</summary>
public sealed record VideoStreamSessionOpenArguments
{
	/// <summary>Minted by the host; every later operation and <c>video-streams</c> call names it.</summary>
	public required string SessionId { get; init; }

	public required string ProviderId { get; init; }

	public required string StreamId { get; init; }

	/// <summary>The transports the consumer can play, most preferred first.</summary>
	public IReadOnlyList<string> AcceptedTransports { get; init; } = [];

	public required VideoStreamConsumerDto Consumer { get; init; }
}

/// <summary>Result of the <c>session.open</c> operation.</summary>
public sealed record VideoStreamSessionOpenResult
{
	public required VideoStreamSessionDescriptionDto Description { get; init; }

	/// <summary>The registration of the provider that opened the session.</summary>
	public required string RegistrationId { get; init; }
}

/// <summary>Arguments for the <c>session.suspend</c> and <c>session.resume</c> operations.</summary>
public sealed record VideoStreamSessionArguments
{
	public required string SessionId { get; init; }

	public required string ProviderId { get; init; }
}

/// <summary>Result of the <c>session.resume</c> operation.</summary>
public sealed record VideoStreamSessionResumeResult
{
	/// <summary>A new description, or absent when the previous one is still valid.</summary>
	public VideoStreamSessionDescriptionDto? Description { get; init; }
}

/// <summary>Arguments for the <c>session.signal</c> operation: a signal from the consumer.</summary>
public sealed record VideoStreamSessionSignalArguments
{
	public required string SessionId { get; init; }

	public required string ProviderId { get; init; }

	public required VideoStreamSignalDto Signal { get; init; }
}

/// <summary>Result of the <c>session.signal</c> operation.</summary>
public sealed record VideoStreamSessionSignalResult
{
	/// <summary>The provider's direct answer, or absent when it has none.</summary>
	public VideoStreamSignalDto? Signal { get; init; }
}

/// <summary>Arguments for the <c>session.close</c> operation.</summary>
public sealed record VideoStreamSessionCloseArguments
{
	public required string SessionId { get; init; }

	public required string ProviderId { get; init; }

	/// <summary>One of the SDK's <c>VideoStreamSessionReason</c> member names. A reader maps a value it
	/// does not know to <c>None</c>.</summary>
	public string Reason { get; init; } = "None";
}
