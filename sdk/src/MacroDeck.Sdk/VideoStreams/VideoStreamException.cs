namespace MacroDeck.Sdk.VideoStreams;

/// <summary>
/// Thrown by an <see cref="IVideoStreamProvider" /> to refuse an operation. The code reaches the consumer;
/// the message is diagnostic only and is not shown to users.
/// </summary>
public sealed class VideoStreamException : Exception
{
	public VideoStreamException(VideoStreamErrorCode errorCode, string message)
		: base(message) => ErrorCode = errorCode;

	public VideoStreamException(VideoStreamErrorCode errorCode, string message, Exception innerException)
		: base(message, innerException) => ErrorCode = errorCode;

	public VideoStreamException()
		: this(VideoStreamErrorCode.Failed, "The video stream operation failed.")
	{
	}

	public VideoStreamException(string message)
		: this(VideoStreamErrorCode.Failed, message)
	{
	}

	public VideoStreamException(string message, Exception innerException)
		: this(VideoStreamErrorCode.Failed, message, innerException)
	{
	}

	public VideoStreamErrorCode ErrorCode { get; }
}
