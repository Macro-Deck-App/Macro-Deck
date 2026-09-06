namespace MacroDeckHost.Application.Ui.Transport.Messages.Host;

public sealed class GetHostSessionRequest;

public sealed class GetHostSessionResponse
{
	public Guid SessionId { get; set; }

	/// <summary>
	/// True when this start applied a staged restore, so a client holding older data is not merely stale
	/// but describing an installation that no longer exists.
	/// </summary>
	public bool RestoreApplied { get; set; }
}
