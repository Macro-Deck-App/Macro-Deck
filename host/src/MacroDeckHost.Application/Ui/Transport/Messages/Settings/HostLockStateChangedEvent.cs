namespace MacroDeckHost.Application.Ui.Transport.Messages.Settings;

public class HostLockStateChangedEvent
{
	public bool Locked { get; set; }

	public bool LockScreenEnabled { get; set; }

	public bool Supported { get; set; }
}
