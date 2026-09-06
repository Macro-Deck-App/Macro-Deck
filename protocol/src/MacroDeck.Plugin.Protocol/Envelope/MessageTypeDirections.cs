namespace MacroDeck.Plugin.Protocol.Envelope;

/// <summary>The expected direction of every known message type, used by drift tests and by #107's
/// transport to reject a message arriving from the wrong side.</summary>
public static class MessageTypeDirections
{
	private static readonly Dictionary<string, MessageDirection> _directions =
		new(StringComparer.Ordinal)
		{
			[MessageTypes.SessionHello] = MessageDirection.PluginToHost,
			[MessageTypes.SessionWelcome] = MessageDirection.HostToPlugin,
			[MessageTypes.SessionGoodbye] = MessageDirection.Bidirectional,
			[MessageTypes.SessionPing] = MessageDirection.Bidirectional,
			[MessageTypes.SessionPong] = MessageDirection.Bidirectional,
			[MessageTypes.CapabilityDeclare] = MessageDirection.PluginToHost,
			[MessageTypes.CapabilityDeclareAck] = MessageDirection.HostToPlugin,
			[MessageTypes.CapabilityInvoke] = MessageDirection.HostToPlugin,
			[MessageTypes.CapabilityResult] = MessageDirection.PluginToHost,
			[MessageTypes.CapabilityCancel] = MessageDirection.HostToPlugin,
			[MessageTypes.HostInvoke] = MessageDirection.PluginToHost,
			[MessageTypes.HostResult] = MessageDirection.HostToPlugin,
			[MessageTypes.HostCancel] = MessageDirection.PluginToHost,
			[MessageTypes.HostState] = MessageDirection.HostToPlugin,
			[MessageTypes.EventPublish] = MessageDirection.PluginToHost,
			[MessageTypes.LogPublish] = MessageDirection.PluginToHost,
			[MessageTypes.StateUpdate] = MessageDirection.PluginToHost,
			[MessageTypes.AssetBegin] = MessageDirection.PluginToHost,
			[MessageTypes.AssetChunk] = MessageDirection.PluginToHost,
			[MessageTypes.AssetCommit] = MessageDirection.PluginToHost,
			[MessageTypes.AssetAck] = MessageDirection.HostToPlugin,
			// A separate host-to-plugin asset pipeline, kept apart from asset.begin/chunk/commit/ack
			// precisely so those existing plugin-to-host types keep their declared direction.
			[MessageTypes.HostAssetBegin] = MessageDirection.HostToPlugin,
			[MessageTypes.HostAssetChunk] = MessageDirection.HostToPlugin,
			[MessageTypes.HostAssetCommit] = MessageDirection.HostToPlugin,
			[MessageTypes.HostAssetAck] = MessageDirection.PluginToHost,
			[MessageTypes.FlowPause] = MessageDirection.Bidirectional,
			[MessageTypes.FlowResume] = MessageDirection.Bidirectional,
			[MessageTypes.ProtocolError] = MessageDirection.Bidirectional,
		};

	public static bool TryGetDirection(string messageType, out MessageDirection direction)
		=> _directions.TryGetValue(messageType, out direction);
}
