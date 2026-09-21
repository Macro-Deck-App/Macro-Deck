namespace MacroDeck.Sdk.Android;

/// <summary>Thrown by an <see cref="IAndroidDevice" /> operation that could not be carried out.</summary>
public sealed class AndroidDeviceException : Exception
{
	public AndroidDeviceException(AndroidDeviceErrorCode errorCode, string message)
		: base(message) => ErrorCode = errorCode;

	public AndroidDeviceException(AndroidDeviceErrorCode errorCode, string message, Exception innerException)
		: base(message, innerException) => ErrorCode = errorCode;

	public AndroidDeviceException()
	{
	}

	public AndroidDeviceException(string message)
		: base(message)
	{
	}

	public AndroidDeviceException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public AndroidDeviceErrorCode ErrorCode { get; }
}
