using MacroDeck.Plugin.Protocol.Envelope;

namespace MacroDeck.Plugin.Protocol.Limits;

/// <summary>
/// Which message types keep flowing while a <c>flow.pause</c> is in effect. The exempt set includes
/// the reply types - <c>capability.result</c>, <c>capability.declare.ack</c>, <c>asset.ack</c> and
/// <c>host.asset.ack</c> -
/// because replies drain the peer's queue rather than growing it; blocking them would leave every
/// in-flight invocation to burn its timeout while the plugin is forbidden from answering.
///
/// <para>
/// <c>host.invoke</c>, <c>host.result</c> and <c>host.cancel</c> are exempt for the same reason,
/// applied to the reverse direction: a plugin capability handler blocked inside a call back into the
/// host is blocked on <c>host.result</c> arriving, and parking its <c>host.invoke</c> (or the
/// cancellation of one) behind a pause would live-lock that handler for as long as the pause lasts.
/// </para>
/// </summary>
public static class ProtocolBackpressure
{
	private static readonly HashSet<string> _exemptWhilePaused = new(StringComparer.Ordinal)
	{
		MessageTypes.CapabilityResult,
		MessageTypes.CapabilityDeclareAck,
		MessageTypes.AssetAck,
		MessageTypes.HostAssetAck,
		MessageTypes.SessionPing,
		MessageTypes.SessionPong,
		MessageTypes.SessionGoodbye,
		MessageTypes.FlowPause,
		MessageTypes.FlowResume,
		MessageTypes.CapabilityCancel,
		MessageTypes.ProtocolError,
		MessageTypes.HostInvoke,
		MessageTypes.HostResult,
		MessageTypes.HostCancel,
	};

	public static bool IsExemptWhilePaused(string messageType) => _exemptWhilePaused.Contains(messageType);
}
