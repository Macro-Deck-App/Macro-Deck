namespace MacroDeckHost.Application.Ui.Transport.Messages.System;

public class GetLockStateResponse
{
	public bool Locked { get; set; }

	public bool LockScreenEnabled { get; set; }

	public bool Supported { get; set; }
}
