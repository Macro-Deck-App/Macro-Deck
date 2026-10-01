using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Capabilities.VideoStreamProvider;
using MacroDeck.Sdk.VideoStreams;

namespace MacroDeckHost.Application.VideoStreams;

public sealed record VideoStreamOpenTicket(string SessionId, long Revision, VideoStreamSessionState State);

public interface IVideoStreamSessionBroker
{
	VideoStreamOpenTicket OpenSession(string connectionId,
		string providerId,
		string streamId,
		IReadOnlyList<string> acceptedTransports);

	void KeepAliveSession(string connectionId, string sessionId);

	void SuspendSession(string connectionId, string sessionId);

	void ResumeSession(string connectionId, string sessionId);

	void CloseSession(string connectionId, string sessionId);

	void CloseConnection(string connectionId, VideoStreamSessionReason reason);

	void ApplyProviderUpdate(string ownerId,
		string sessionId,
		VideoStreamSessionState state,
		VideoStreamSessionDescriptionDto? description,
		VideoStreamSessionReason reason,
		LocalizedText? message);

	bool ApplyProviderClose(string ownerId, string sessionId, VideoStreamSessionReason reason, LocalizedText? message);

	void ClosePluginSession(string pluginSessionId, VideoStreamSessionReason reason);

	void CloseOwner(string ownerId, VideoStreamSessionReason reason, bool notifyProvider);

	Task ShutdownAsync(TimeSpan bound);
}
