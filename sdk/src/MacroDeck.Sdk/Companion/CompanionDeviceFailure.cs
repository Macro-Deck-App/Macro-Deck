namespace MacroDeck.Sdk.Companion;

/// <summary>Why a request to a Companion device failed. Later versions may add values; treat an unknown one like <see cref="Failed" />.</summary>
public enum CompanionDeviceFailure
{
	/// <summary>The device is not connected, or disconnected before it answered.</summary>
	NotConnected,

	/// <summary>The user removed the device while the request was running.</summary>
	Removed,

	/// <summary>The device did not answer in time.</summary>
	TimedOut,

	/// <summary>The device's user declined the permission the request needs.</summary>
	ConsentDenied,

	/// <summary>The device cannot do this right now, for example a deck screenshot while the deck is not on screen.</summary>
	Unavailable,

	/// <summary>The device tried and failed.</summary>
	Failed,

	/// <summary>This plugin may not use Companion devices, see <see cref="ICompanionDeviceApi.Access" />.</summary>
	NotAllowed
}
