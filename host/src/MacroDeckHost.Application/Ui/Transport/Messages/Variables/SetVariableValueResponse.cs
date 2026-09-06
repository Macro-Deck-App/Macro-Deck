namespace MacroDeckHost.Application.Ui.Transport.Messages.Variables;

public class SetVariableValueResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public Variable? Variable { get; set; }

	/// <summary>
	/// True when the owning provider accepted the write but the host has not yet read the result back.
	/// <see cref="Variable"/> then still carries the value from before the write - the authoritative one
	/// arrives on the read side - so a client must not treat it as the value it just set.
	/// </summary>
	public bool Pending { get; set; }
}
