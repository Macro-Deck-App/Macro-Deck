namespace MacroDeckHost.Integrations.Clipboard.Native;

internal enum ClipboardFailure
{
	Failed,
	Locked,
	TooLarge,
	UnsupportedFormat,
	NotFound,
	Refused,
	Unavailable
}

internal sealed class ClipboardException : Exception
{
	public ClipboardException(ClipboardFailure failure)
		: this(failure, $"Clipboard operation failed: {failure}")
	{
	}

	public ClipboardException(ClipboardFailure failure, string message, Exception? innerException = null)
		: base(message, innerException)
	{
		Failure = failure;
	}

	public ClipboardFailure Failure { get; }
}
