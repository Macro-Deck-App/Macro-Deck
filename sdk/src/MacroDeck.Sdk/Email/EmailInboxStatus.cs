namespace MacroDeck.Sdk.Email;

/// <summary>Whether an inbox's data is current, as an <see cref="EmailInboxSnapshot" /> reports it.</summary>
public enum EmailInboxStatus
{
	/// <summary>Not connected yet, or reconnecting after a short interruption. The data may be old.</summary>
	Connecting = 0,

	/// <summary>Connected; the data is current.</summary>
	Connected = 1,

	/// <summary>Not connected and not expected to be soon, e.g. the server cannot be reached. The data is
	/// the last known.</summary>
	Disconnected = 2,

	/// <summary>Not connected because something needs the user's attention, e.g. the sign-in was refused.
	/// The data is the last known.</summary>
	Error = 3
}
