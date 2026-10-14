namespace MacroDeck.Sdk.Batteries;

/// <summary>The <see cref="IBatteryApi" /> of a context that offers no battery devices: always empty, never raises <see cref="Changed" />.</summary>
public sealed class UnsupportedBatteryApi : IBatteryApi
{
	public static readonly UnsupportedBatteryApi Instance = new();

	private UnsupportedBatteryApi()
	{
	}

	public IReadOnlyList<BatteryDeviceSnapshot> Devices => [];

	public BatteryDeviceSnapshot? Find(string id)
	{
		return null;
	}

	public event EventHandler<BatteryDevicesChangedEventArgs>? Changed
	{
		add { }
		remove { }
	}
}
