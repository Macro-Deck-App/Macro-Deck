namespace MacroDeck.Plugin.Protocol.Envelope;

/// <summary>
/// Every message type v1 speaks. Not an enum: <see cref="System.Text.Json.Serialization.JsonStringEnumConverter" />
/// throws on an unrecognised value, which would turn "unknown message type" into a parse exception -
/// exactly the behaviour the wire contract forbids. An unknown type must be reportable, never fatal.
/// </summary>
public static class MessageTypes
{
	public const string SessionHello = "session.hello";

	public const string SessionWelcome = "session.welcome";

	public const string SessionGoodbye = "session.goodbye";

	public const string SessionPing = "session.ping";

	public const string SessionPong = "session.pong";

	public const string CapabilityDeclare = "capability.declare";

	public const string CapabilityDeclareAck = "capability.declare.ack";

	public const string CapabilityInvoke = "capability.invoke";

	public const string CapabilityResult = "capability.result";

	public const string CapabilityCancel = "capability.cancel";

	/// <summary>A plugin calling a host API - the reverse direction of <see cref="CapabilityInvoke" />.</summary>
	public const string HostInvoke = "host.invoke";

	/// <summary>The host's reply to <see cref="HostInvoke" />.</summary>
	public const string HostResult = "host.result";

	/// <summary>Best-effort cancellation of a <see cref="HostInvoke" />, mirroring <see cref="CapabilityCancel" />.</summary>
	public const string HostCancel = "host.cancel";

	/// <summary>The host pushing the list a plugin's synchronous API members serve from.</summary>
	public const string HostState = "host.state";

	public const string EventPublish = "event.publish";

	/// <summary>A plugin forwarding a batch of structured log events. Fire-and-forget.</summary>
	public const string LogPublish = "log.publish";

	public const string StateUpdate = "state.update";

	public const string AssetBegin = "asset.begin";

	public const string AssetChunk = "asset.chunk";

	public const string AssetCommit = "asset.commit";

	public const string AssetAck = "asset.ack";

	// A separate host-to-plugin asset pipeline, not a reuse of asset.begin/chunk/commit/ack: those four
	// are declared plugin-to-host (asset.ack the lone hostToPlugin reply) and must keep that direction,
	// since flipping it would silently change the meaning of an existing v1 message type. host.asset.*
	// exists so the host can push bytes - e.g. an icon a device session requested - without touching
	// the plugin-to-host set's direction at all.

	public const string HostAssetBegin = "host.asset.begin";

	public const string HostAssetChunk = "host.asset.chunk";

	public const string HostAssetCommit = "host.asset.commit";

	public const string HostAssetAck = "host.asset.ack";

	public const string FlowPause = "flow.pause";

	public const string FlowResume = "flow.resume";

	public const string ProtocolError = "protocol.error";

	public static readonly IReadOnlyList<string> All =
	[
		SessionHello, SessionWelcome, SessionGoodbye, SessionPing, SessionPong,
		CapabilityDeclare, CapabilityDeclareAck, CapabilityInvoke, CapabilityResult, CapabilityCancel,
		HostInvoke, HostResult, HostCancel, HostState,
		EventPublish, LogPublish, StateUpdate,
		AssetBegin, AssetChunk, AssetCommit, AssetAck,
		HostAssetBegin, HostAssetChunk, HostAssetCommit, HostAssetAck,
		FlowPause, FlowResume,
		ProtocolError,
	];

	private static readonly HashSet<string> _knownTypes = new(All, StringComparer.Ordinal);

	// The reply types only. protocol.error is deliberately excluded here - its correlation is
	// conditional on whether it answers a specific message, which this flat predicate cannot express;
	// Correlation.CorrelationRules handles that case instead.
	private static readonly HashSet<string> _replyTypes = new(StringComparer.Ordinal)
	{
		CapabilityResult, CapabilityDeclareAck, AssetAck, HostResult, HostAssetAck,
	};

	public static bool IsKnown(string? messageType) => messageType is not null && _knownTypes.Contains(messageType);

	public static bool RequiresCorrelation(string messageType) => _replyTypes.Contains(messageType);
}
