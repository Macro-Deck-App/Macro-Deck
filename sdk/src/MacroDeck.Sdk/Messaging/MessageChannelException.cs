namespace MacroDeck.Sdk.Messaging;

/// <summary>Thrown by an <see cref="IMessageChannel" /> operation that could not be carried out.</summary>
public sealed class MessageChannelException : Exception
{
	public MessageChannelException(MessageChannelErrorCode errorCode, string? topic, string message)
		: base(message)
	{
		ErrorCode = errorCode;
		Topic = topic;
	}

	public MessageChannelException(MessageChannelErrorCode errorCode,
		string? topic,
		string message,
		string? handlerOwner)
		: this(errorCode, topic, message) => HandlerOwner = handlerOwner;

	public MessageChannelException()
	{
	}

	public MessageChannelException(string message)
		: base(message)
	{
	}

	public MessageChannelException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public MessageChannelErrorCode ErrorCode { get; }

	/// <summary>The topic or pattern the operation was about, when there is one.</summary>
	public string? Topic { get; }

	/// <summary>For <see cref="MessageChannelErrorCode.TopicAlreadyHandled" />: the integration id that handles the topic.</summary>
	public string? HandlerOwner { get; }
}
