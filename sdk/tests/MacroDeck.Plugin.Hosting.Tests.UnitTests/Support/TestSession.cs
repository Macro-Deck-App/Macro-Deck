using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;

/// <summary>Builds the session and dispatcher a connection test needs, with the real protocol limits.</summary>
internal static class TestSession
{
	public static PluginSession Create(TimeSpan? keepAliveInterval = null, int? maxInboundQueueDepth = null)
		=> new("session-1",
			"token-1",
			1,
			Limits(maxInboundQueueDepth),
			Timeouts(keepAliveInterval),
			DateTimeOffset.UnixEpoch);

	public static CapabilityDispatcher Dispatcher(params ICapabilityHandler[] handlers)
	{
		var services = new ServiceCollection();
		services.AddScoped<CapabilityInvocationContextHolder>();

		return new CapabilityDispatcher(new CapabilityCatalog(handlers),
			services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
			new PluginConnectionState(),
			TimeProvider.System,
			Serilog.Core.Logger.None);
	}

	public static PluginProtocolLimitsDescriptor Limits(int? maxInboundQueueDepth = null) => new()
	{
		MaxMessageBytes = ProtocolLimits.MaxMessageBytes,
		MaxAssetBytes = ProtocolLimits.MaxAssetBytes,
		MaxAssetChunkBytes = ProtocolLimits.MaxAssetChunkBytes,
		MaxInboundQueueDepth = maxInboundQueueDepth ?? ProtocolLimits.MaxInboundQueueDepth,
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
		MaxLogEventBurst = ProtocolLimits.MaxLogEventBurst
	};

	public static PluginProtocolTimeoutsDescriptor Timeouts(TimeSpan? keepAliveInterval = null) => new()
	{
		Handshake = ProtocolTimeouts.Handshake,
		DefaultRequest = ProtocolTimeouts.DefaultRequest,
		CapabilityInvoke = ProtocolTimeouts.CapabilityInvoke,
		AssetUpload = ProtocolTimeouts.AssetUpload,
		KeepAliveInterval = keepAliveInterval ?? ProtocolTimeouts.KeepAliveInterval,
		KeepAliveTimeout = ProtocolTimeouts.KeepAliveTimeout,
		SessionResumeWindow = ProtocolTimeouts.SessionResumeWindow,
		GracefulClose = ProtocolTimeouts.GracefulClose
	};
}
