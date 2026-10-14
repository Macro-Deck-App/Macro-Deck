namespace MacroDeck.Sdk.Notifications;

/// <summary>Thrown by an <see cref="INotificationService" /> operation that could not be carried out.</summary>
public sealed class NotificationException : Exception
{
	public NotificationException(NotificationErrorCode errorCode, string message)
		: base(message)
	{
		ErrorCode = errorCode;
	}

	public NotificationException()
	{
	}

	public NotificationException(string message)
		: base(message)
	{
	}

	public NotificationException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public NotificationErrorCode ErrorCode { get; }
}
