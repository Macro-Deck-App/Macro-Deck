namespace MacroDeck.Sdk.Devices;

/// <summary>Whether a provider-registered device is reachable right now.</summary>
public enum DevicePresence
{
	/// <summary>The provider cannot tell. The host treats this as offline for display purposes.</summary>
	Unknown = 0,

	/// <summary>The device is connected and reachable.</summary>
	Online = 1,

	/// <summary>The device is known but not currently reachable. Its registration is retained.</summary>
	Offline = 2
}
