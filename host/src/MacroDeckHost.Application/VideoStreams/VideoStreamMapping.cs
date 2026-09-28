using MacroDeck.Sdk.VideoStreams;

namespace MacroDeckHost.Application.VideoStreams;

public static class VideoStreamMapping
{
	public static VideoStreamError ToBrokerError(VideoStreamErrorCode code)
		=> code switch
		{
			VideoStreamErrorCode.UnknownProvider => VideoStreamError.UnknownProvider,
			VideoStreamErrorCode.UnknownStream => VideoStreamError.UnknownStream,
			VideoStreamErrorCode.UnknownSession => VideoStreamError.UnknownSession,
			VideoStreamErrorCode.StreamUnavailable => VideoStreamError.StreamUnavailable,
			VideoStreamErrorCode.TransportNotAccepted => VideoStreamError.TransportNotAccepted,
			VideoStreamErrorCode.CapacityReached => VideoStreamError.Busy,
			VideoStreamErrorCode.SignalingUnsupported => VideoStreamError.SignalingUnsupported,
			VideoStreamErrorCode.Busy => VideoStreamError.Busy,
			VideoStreamErrorCode.Unsupported => VideoStreamError.ProviderUnavailable,
			_ => VideoStreamError.Failed
		};
}
