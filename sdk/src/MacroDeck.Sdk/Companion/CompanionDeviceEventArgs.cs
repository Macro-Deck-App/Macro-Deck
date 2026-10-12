namespace MacroDeck.Sdk.Companion;

public sealed class CompanionDeviceEventArgs : EventArgs
{
	public CompanionDeviceEventArgs(Guid deviceId, CompanionDevice? device)
	{
		DeviceId = deviceId;
		Device = device;
	}

	public Guid DeviceId { get; }

	/// <summary>The device as it is now, or <c>null</c> when it disconnected or was removed.</summary>
	public CompanionDevice? Device { get; }
}
