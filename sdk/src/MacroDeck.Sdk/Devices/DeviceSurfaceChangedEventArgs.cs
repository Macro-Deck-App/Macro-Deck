namespace MacroDeck.Sdk.Devices;

/// <summary>Raised by <see cref="IDeviceSession.SurfaceChanged" /> with the newly pushed surface.</summary>
public sealed class DeviceSurfaceChangedEventArgs : EventArgs
{
	public DeviceSurfaceChangedEventArgs(DeviceSurface surface)
	{
		Surface = surface;
	}

	public DeviceSurface Surface { get; }
}
