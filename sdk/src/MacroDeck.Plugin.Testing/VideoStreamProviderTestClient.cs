using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.VideoStreamProvider;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing.Internal;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// The <c>video-stream-provider</c> capability, driven the way the host drives it: list the providers and
/// their streams, then open, suspend, resume and close sessions. Results deserialize to the
/// <c>MacroDeck.Plugin.Protocol.Capabilities.VideoStreamProvider</c> DTOs, and a rejection arrives as a
/// capability failure whose <c>reason</c> detail is one of the <c>video_stream_</c> values of
/// <c>ProtocolErrorReasons</c>.
/// </summary>
public sealed class VideoStreamProviderTestClient
{
	private readonly ICapabilityInvoker _invoker;

	internal VideoStreamProviderTestClient(ICapabilityInvoker invoker) => _invoker = invoker;

	/// <summary>The providers the plugin has registered. Deserializes to
	/// <see cref="VideoStreamProviderDescribePayload" />.</summary>
	public Task<CapabilityInvocationOutcome> DescribeAsync(CapabilityInvokeOptions? options = null)
		=> Invoke(CapabilityOperations.VideoStreamProvider.Describe, null, options);

	/// <summary>One provider's streams. Deserializes to <see cref="VideoStreamProviderStreamsResult" />.</summary>
	public Task<CapabilityInvocationOutcome> GetStreamsAsync(string providerId, CapabilityInvokeOptions? options = null)
		=> Invoke(CapabilityOperations.VideoStreamProvider.Streams,
			new VideoStreamProviderStreamsArguments { ProviderId = providerId },
			options);

	/// <summary>
	/// Opens a session. Deserializes to <see cref="VideoStreamSessionOpenResult" />.
	/// </summary>
	/// <param name="sessionId">Pick a fresh one per open: an id that was closed before is refused, exactly
	/// as a real host's reuse would be.</param>
	/// <param name="acceptedTransports">The transports the pretend consumer plays, most preferred first.</param>
	public Task<CapabilityInvocationOutcome> OpenSessionAsync(
		string sessionId,
		string providerId,
		string streamId,
		IReadOnlyList<string> acceptedTransports,
		CapabilityInvokeOptions? options = null)
		=> Invoke(CapabilityOperations.VideoStreamProvider.SessionOpen,
			new VideoStreamSessionOpenArguments
			{
				SessionId = sessionId,
				ProviderId = providerId,
				StreamId = streamId,
				AcceptedTransports = acceptedTransports
			},
			options);

	/// <summary>Suspends an open session.</summary>
	public Task<CapabilityInvocationOutcome> SuspendSessionAsync(
		string sessionId,
		string providerId,
		CapabilityInvokeOptions? options = null)
		=> Invoke(CapabilityOperations.VideoStreamProvider.SessionSuspend,
			new VideoStreamSessionArguments { SessionId = sessionId, ProviderId = providerId },
			options);

	/// <summary>Resumes a suspended session. Deserializes to <see cref="VideoStreamSessionResumeResult" />.</summary>
	public Task<CapabilityInvocationOutcome> ResumeSessionAsync(
		string sessionId,
		string providerId,
		CapabilityInvokeOptions? options = null)
		=> Invoke(CapabilityOperations.VideoStreamProvider.SessionResume,
			new VideoStreamSessionArguments { SessionId = sessionId, ProviderId = providerId },
			options);

	/// <summary>Closes a session. Closing one that is already closed, or not open yet, succeeds; a close
	/// that arrives before its open makes that open fail.</summary>
	/// <param name="reason">One of the SDK's <c>VideoStreamSessionReason</c> member names.</param>
	public Task<CapabilityInvocationOutcome> CloseSessionAsync(
		string sessionId,
		string providerId,
		string reason = "ConsumerClosed",
		CapabilityInvokeOptions? options = null)
		=> Invoke(CapabilityOperations.VideoStreamProvider.SessionClose,
			new VideoStreamSessionCloseArguments { SessionId = sessionId, ProviderId = providerId, Reason = reason },
			options);

	private Task<CapabilityInvocationOutcome> Invoke(string operation, object? arguments, CapabilityInvokeOptions? options)
		=> _invoker.InvokeAsync(CapabilityKinds.VideoStreamProvider,
			ProviderCapabilityId.LocalId,
			operation,
			arguments,
			options);
}
