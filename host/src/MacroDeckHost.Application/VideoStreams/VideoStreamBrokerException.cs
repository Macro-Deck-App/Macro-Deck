namespace MacroDeckHost.Application.VideoStreams;

public sealed class VideoStreamBrokerException : Exception
{
	public VideoStreamBrokerException(VideoStreamError error, string message)
		: base(message) => Error = error;

	public VideoStreamError Error { get; }
}
