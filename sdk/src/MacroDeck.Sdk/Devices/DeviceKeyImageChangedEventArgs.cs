namespace MacroDeck.Sdk.Devices;

/// <summary>Raised by <see cref="IDeviceSession.KeyImageChanged" /> with one cell's new picture.</summary>
public sealed class DeviceKeyImageChangedEventArgs : EventArgs
{
	public DeviceKeyImageChangedEventArgs(DeviceKeyImage image)
	{
		Image = image;
	}

	public DeviceKeyImage Image { get; }
}
