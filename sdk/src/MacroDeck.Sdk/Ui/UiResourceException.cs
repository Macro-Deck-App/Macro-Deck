namespace MacroDeck.Sdk.Ui;

/// <summary>Thrown by an <see cref="IUiResourceRegistry" /> operation that Macro Deck refused or that could
/// not be completed.</summary>
public sealed class UiResourceException : Exception
{
	public UiResourceException(UiResourceErrorCode errorCode, string message, Exception? innerException = null)
		: base(message, innerException) => ErrorCode = errorCode;

	public UiResourceException()
	{
	}

	public UiResourceException(string message)
		: base(message)
	{
	}

	public UiResourceException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public UiResourceErrorCode ErrorCode { get; }
}
