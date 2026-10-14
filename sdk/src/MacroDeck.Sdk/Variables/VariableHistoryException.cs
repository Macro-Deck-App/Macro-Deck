namespace MacroDeck.Sdk.Variables;

/// <summary>Why <see cref="IVariableApi.OpenHistoryAsync" /> could not open a window.</summary>
public enum VariableHistoryErrorCode
{
	/// <summary>This Macro Deck, or this context, keeps no variable history.</summary>
	Unsupported = 0,

	/// <summary>The name is not one of the integration's own global variables: it belongs to someone else or
	/// is not a valid variable name.</summary>
	NotOwned = 1,

	/// <summary>The integration already holds as many history windows as Macro Deck allows. Dispose one
	/// first.</summary>
	LimitExceeded = 2,

	/// <summary>The window could not be opened, for example because the connection to Macro Deck dropped.
	/// Retrying later can succeed.</summary>
	Failed = 3
}

/// <summary>Thrown by <see cref="IVariableApi.OpenHistoryAsync" /> when no window could be opened.</summary>
public sealed class VariableHistoryException : Exception
{
	public VariableHistoryException(
		VariableHistoryErrorCode errorCode,
		string message,
		Exception? innerException = null)
		: base(message, innerException)
	{
		ErrorCode = errorCode;
	}

	public VariableHistoryException()
	{
	}

	public VariableHistoryException(string message)
		: base(message)
	{
	}

	public VariableHistoryException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public VariableHistoryErrorCode ErrorCode { get; }
}
