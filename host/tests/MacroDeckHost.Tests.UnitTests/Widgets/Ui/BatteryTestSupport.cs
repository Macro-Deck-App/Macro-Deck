using MacroDeck.Sdk.Batteries;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

internal sealed class FakeBatteries : IBatteryApi
{
	private IReadOnlyList<BatteryDeviceSnapshot> _devices = [];

	public IReadOnlyList<BatteryDeviceSnapshot> Devices => _devices;

	public int SubscriberCount => Changed?.GetInvocationList().Length ?? 0;

	public event EventHandler<BatteryDevicesChangedEventArgs>? Changed;

	public BatteryDeviceSnapshot? Find(string id)
	{
		return _devices.FirstOrDefault(device => device.Id == id);
	}

	public void Set(params BatteryDeviceSnapshot[] devices)
	{
		_devices = [.. devices.OrderBy(device => device.Id, StringComparer.Ordinal)];
		Changed?.Invoke(this, new BatteryDevicesChangedEventArgs([]));
	}

	public static BatteryDeviceSnapshot Device(
		string localId,
		string name,
		int? level,
		string kind = BatteryDeviceKinds.Phone,
		BatteryChargeState state = BatteryChargeState.Discharging,
		bool connected = true,
		string provider = "adb")
	{
		return new BatteryDeviceSnapshot
		{
			Id = $"{provider}:{localId}",
			LocalId = localId,
			ProviderId = provider,
			ProviderName = provider == "adb" ? "ADB" : provider,
			Name = name,
			Kind = kind,
			LevelPercent = level,
			ChargeState = state,
			IsConnected = connected
		};
	}
}
