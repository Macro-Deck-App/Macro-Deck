namespace MacroDeck.Sdk.Devices;

/// <summary>Raised by <see cref="IDeviceSession.Closed" /> when a session ends.</summary>
public sealed class DeviceSessionClosedEventArgs : EventArgs
{
	public DeviceSessionClosedEventArgs(string? reason)
	{
		Reason = reason;
	}

	public string? Reason { get; }
}
