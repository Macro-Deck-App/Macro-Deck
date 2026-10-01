using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Capabilities.VideoStreamProvider;
using MacroDeck.Sdk.VideoStreams;

namespace MacroDeckHost.Application.VideoStreams;

internal sealed record VideoStreamProviderInfo(
	string ProviderId,
	LocalizedText Name,
	LocalizedText? Description,
	string RegistrationId);

internal sealed record VideoStreamOpenResult(VideoStreamSessionDescriptionDto Description, string RegistrationId);

internal interface IVideoStreamEndpoint
{
	Task<IReadOnlyList<VideoStreamProviderInfo>> DescribeAsync(CancellationToken cancellationToken);

	Task<IReadOnlyList<VideoStreamDescriptor>> GetStreamsAsync(string providerId, CancellationToken cancellationToken);

	Task<VideoStreamOpenResult> OpenAsync(string providerId,
		VideoStreamOpenRequest request,
		Func<bool> proceed,
		CancellationToken cancellationToken);

	Task SuspendAsync(string providerId, string sessionId, Func<bool> proceed, CancellationToken cancellationToken);

	Task<VideoStreamSessionDescriptionDto?> ResumeAsync(string providerId,
		string sessionId,
		Func<bool> proceed,
		CancellationToken cancellationToken);

	Task CloseAsync(string providerId,
		string sessionId,
		VideoStreamSessionReason reason,
		CancellationToken cancellationToken);
}
