namespace MacroDeck.Plugin.Protocol.Handshake;

/// <summary>
/// The response of the unauthenticated <c>GET /api/plugins/protocol</c> discovery endpoint: supported
/// versions, capability kinds, limits and timeouts, so a plugin can decide whether and how to register
/// before it holds any credential.
/// </summary>
public sealed record PluginProtocolDescriptor
{
	public required IReadOnlyList<int> SupportedVersions { get; init; }

	public required IReadOnlyList<string> CapabilityKinds { get; init; }

	public required PluginProtocolLimitsDescriptor Limits { get; init; }

	public required PluginProtocolTimeoutsDescriptor Timeouts { get; init; }

	/// <summary>Absent means the host does not support interactive pairing. Not required, so an older
	/// host that omits it still deserialises - this property must never become a compatibility
	/// break for existing plugins.</summary>
	public PluginPairingDescriptor? Pairing { get; init; }

	/// <summary>Absent means the host does not report anything about enrolment. Not required, so an
	/// older host that omits it still deserialises - this property must never become a compatibility
	/// break for existing plugins.</summary>
	public PluginEnrollmentDescriptor? Enrollment { get; init; }
}

/// <summary>The enrolment block of <see cref="PluginProtocolDescriptor" />: what a plugin holding a
/// Developer token can expect from <c>POST /api/plugins/registration</c> before it tries.</summary>
public sealed record PluginEnrollmentDescriptor
{
	/// <summary>Whether the host would accept an enrolment right now. Developer Mode is a live setting
	/// a user can flip at any time, and a host with it switched off refuses both enrolment and session
	/// creation for a plugin holding a development credential. Null means the host does not report it,
	/// so a caller must attempt the request to find out; not required, so an older host that omits it
	/// still deserialises - this property must never become a compatibility break for existing
	/// plugins.</summary>
	public bool? DeveloperModeEnabled { get; init; }
}

/// <summary>Wire copy of <c>Limits.ProtocolLimits</c>, so a plugin reads the limits instead of
/// hard-coding them.</summary>
public sealed record PluginProtocolLimitsDescriptor
{
	public required int MaxMessageBytes { get; init; }

	public required int MaxAssetBytes { get; init; }

	public required int MaxAssetChunkBytes { get; init; }

	public required int MaxInboundQueueDepth { get; init; }

	public required int MaxOutboundQueueDepth { get; init; }

	public required int QueueHighWatermark { get; init; }

	public required int QueueLowWatermark { get; init; }

	public required int MaxConcurrentInvocations { get; init; }

	public required int MaxDeclaredCapabilities { get; init; }

	public required int MaxIdempotencyKeyLength { get; init; }

	public required int MaxErrorMessageLength { get; init; }

	public required int MaxErrorDetailEntries { get; init; }

	public required int MaxJsonDepth { get; init; }

	public required int MaxSessionsPerPlugin { get; init; }

	public required int MaxLogEventsPerBatch { get; init; }

	public required int MaxLogMessageLength { get; init; }

	public required int MaxLogPropertiesPerEvent { get; init; }

	public required int MaxLogPropertyNameLength { get; init; }

	public required int MaxLogPropertyValueLength { get; init; }

	public required int MaxLogSourceContextLength { get; init; }

	public required int MaxLogExceptionLength { get; init; }

	public required int MaxLogExceptionDepth { get; init; }

	public required int MaxLogInboundQueueDepth { get; init; }

	public required int MaxLogEventsPerSecond { get; init; }

	public required int MaxLogEventBurst { get; init; }

	/// <summary>The UI-session limits. Not required, so a plugin on a newer protocol package still
	/// deserialises an older host's response that omits them - these properties must never become a
	/// compatibility break for existing plugins.</summary>
	public int? MaxUiTreeBytes { get; init; }

	public int? MaxUiPatchBytes { get; init; }

	public int? MaxUiNodesPerTree { get; init; }

	public int? MaxUiUpdatesPerSecond { get; init; }

	public int? MaxUiUpdateBurst { get; init; }

	public int? MaxUiResourceBytes { get; init; }

	public int? MaxUiAttachmentsPerSession { get; init; }

	public int? MaxUiSessionsPerProvider { get; init; }

	public int? MaxUiWidgetSessionsPerProvider { get; init; }
}

/// <summary>Wire copy of <c>Limits.ProtocolTimeouts</c>.</summary>
public sealed record PluginProtocolTimeoutsDescriptor
{
	public required TimeSpan Handshake { get; init; }

	public required TimeSpan DefaultRequest { get; init; }

	public required TimeSpan CapabilityInvoke { get; init; }

	public required TimeSpan AssetUpload { get; init; }

	public required TimeSpan KeepAliveInterval { get; init; }

	public required TimeSpan KeepAliveTimeout { get; init; }

	public required TimeSpan SessionResumeWindow { get; init; }

	public required TimeSpan GracefulClose { get; init; }
}
