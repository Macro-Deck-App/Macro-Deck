namespace MacroDeck.Sdk.Android;

public sealed class AndroidDeviceEventArgs : EventArgs
{
	public AndroidDeviceEventArgs(IAndroidDevice device, AndroidDeviceState? previousState)
	{
		Device = device;
		PreviousState = previousState;
	}

	public IAndroidDevice Device { get; }

	/// <summary>The state before a <see cref="IAndroidDeviceManager.DeviceStateChanged" />; <c>null</c> for the other events.</summary>
	public AndroidDeviceState? PreviousState { get; }
}
