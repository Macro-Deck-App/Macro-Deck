using MacroDeck.Sdk.VideoStreams;

namespace MacroDeckHost.Application.VideoStreams;

internal enum VideoStreamEndpointFailure
{
	Rejected,
	RateLimited,
	TimedOut,
	Unavailable,
	Skipped
}

internal sealed class VideoStreamEndpointException : Exception
{
	public VideoStreamEndpointException(VideoStreamEndpointFailure failure, VideoStreamErrorCode code, string message)
		: base(message)
	{
		Failure = failure;
		Code = code;
	}

	public VideoStreamEndpointFailure Failure { get; }

	public VideoStreamErrorCode Code { get; }

	public static VideoStreamEndpointException Skipped()
		=> new(VideoStreamEndpointFailure.Skipped,
			VideoStreamErrorCode.UnknownSession,
			"The session ended before the call was sent.");
}
