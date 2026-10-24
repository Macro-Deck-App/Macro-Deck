namespace MacroDeck.Sdk.Ui;

/// <summary>
/// Thrown by <see cref="IUiEventOrigin.ReadUploadAsync" /> when Macro Deck holds no upload under that id for
/// the session the event came from: it expired, a newer upload of the same input replaced it, the session
/// closed, or it belongs to another session. Ignore the event that named it; a newer one follows when the
/// user edits again.
/// </summary>
public sealed class UiUploadNotFoundException : Exception
{
	public UiUploadNotFoundException()
		: base("Macro Deck holds no such upload for this session.")
	{
	}

	public UiUploadNotFoundException(string message)
		: base(message)
	{
	}

	public UiUploadNotFoundException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
