using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Protocol.Handshake;

/// <summary>
/// The one projection of <see cref="ProtocolVersions"/>/<see cref="CapabilityKinds"/>/
/// <see cref="ProtocolLimits"/>/<see cref="ProtocolTimeouts"/> into their wire descriptors. Used by
/// both the discovery endpoint and the session response, so the mapping never drifts into two copies.
/// </summary>
public static class ProtocolDescriptorFactory
{
	public static PluginProtocolDescriptor CreateProtocolDescriptor() => new()
	{
		SupportedVersions = ProtocolVersions.Supported,
		CapabilityKinds = CapabilityKinds.All,
		Limits = CreateLimitsDescriptor(),
		Timeouts = CreateTimeoutsDescriptor()
	};

	public static PluginProtocolLimitsDescriptor CreateLimitsDescriptor() => new()
	{
		MaxMessageBytes = ProtocolLimits.MaxMessageBytes,
		MaxAssetBytes = ProtocolLimits.MaxAssetBytes,
		MaxAssetChunkBytes = ProtocolLimits.MaxAssetChunkBytes,
		MaxInboundQueueDepth = ProtocolLimits.MaxInboundQueueDepth,
		MaxOutboundQueueDepth = ProtocolLimits.MaxOutboundQueueDepth,
		QueueHighWatermark = ProtocolLimits.QueueHighWatermark,
		QueueLowWatermark = ProtocolLimits.QueueLowWatermark,
		MaxConcurrentInvocations = ProtocolLimits.MaxConcurrentInvocations,
		MaxDeclaredCapabilities = ProtocolLimits.MaxDeclaredCapabilities,
		MaxIdempotencyKeyLength = ProtocolLimits.MaxIdempotencyKeyLength,
		MaxErrorMessageLength = ProtocolLimits.MaxErrorMessageLength,
		MaxErrorDetailEntries = ProtocolLimits.MaxErrorDetailEntries,
		MaxJsonDepth = ProtocolLimits.MaxJsonDepth,
		MaxSessionsPerPlugin = ProtocolLimits.MaxSessionsPerPlugin,
		MaxLogEventsPerBatch = ProtocolLimits.MaxLogEventsPerBatch,
		MaxLogMessageLength = ProtocolLimits.MaxLogMessageLength,
		MaxLogPropertiesPerEvent = ProtocolLimits.MaxLogPropertiesPerEvent,
		MaxLogPropertyNameLength = ProtocolLimits.MaxLogPropertyNameLength,
		MaxLogPropertyValueLength = ProtocolLimits.MaxLogPropertyValueLength,
		MaxLogSourceContextLength = ProtocolLimits.MaxLogSourceContextLength,
		MaxLogExceptionLength = ProtocolLimits.MaxLogExceptionLength,
		MaxLogExceptionDepth = ProtocolLimits.MaxLogExceptionDepth,
		MaxLogInboundQueueDepth = ProtocolLimits.MaxLogInboundQueueDepth,
		MaxLogEventsPerSecond = ProtocolLimits.MaxLogEventsPerSecond,
		MaxLogEventBurst = ProtocolLimits.MaxLogEventBurst,
		MaxUiTreeBytes = ProtocolLimits.MaxUiTreeBytes,
		MaxUiPatchBytes = ProtocolLimits.MaxUiPatchBytes,
		MaxUiNodesPerTree = ProtocolLimits.MaxUiNodesPerTree,
		MaxUiUpdatesPerSecond = ProtocolLimits.MaxUiUpdatesPerSecond,
		MaxUiUpdateBurst = ProtocolLimits.MaxUiUpdateBurst,
		MaxUiResourceBytes = ProtocolLimits.MaxUiResourceBytes,
		MaxUiAttachmentsPerSession = ProtocolLimits.MaxUiAttachmentsPerSession,
		MaxUiSessionsPerProvider = ProtocolLimits.MaxUiSessionsPerProvider,
		MaxUiWidgetSessionsPerProvider = ProtocolLimits.MaxUiWidgetSessionsPerProvider
	};

	public static PluginProtocolTimeoutsDescriptor CreateTimeoutsDescriptor() => new()
	{
		Handshake = ProtocolTimeouts.Handshake,
		DefaultRequest = ProtocolTimeouts.DefaultRequest,
		CapabilityInvoke = ProtocolTimeouts.CapabilityInvoke,
		AssetUpload = ProtocolTimeouts.AssetUpload,
		KeepAliveInterval = ProtocolTimeouts.KeepAliveInterval,
		KeepAliveTimeout = ProtocolTimeouts.KeepAliveTimeout,
		SessionResumeWindow = ProtocolTimeouts.SessionResumeWindow,
		GracefulClose = ProtocolTimeouts.GracefulClose
	};
}
