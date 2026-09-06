namespace MacroDeckHost.Application.Lifecycle;

/// <summary>
/// Identifies this run of the host. A client that reconnects and sees a different id is talking to a host
/// that started since it last looked, so anything it cached may no longer describe the installation - most
/// sharply after a restore, which replaces the data underneath it.
/// </summary>
public sealed class HostSession
{
	public Guid Id { get; } = Guid.NewGuid();

	public bool RestoreApplied { get; private set; }

	public void MarkRestoreApplied() => RestoreApplied = true;
}
