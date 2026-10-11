namespace MacroDeck.Sdk.Batteries;

/// <summary>
/// Reads every battery device any provider published to Macro Deck, including other plugins' and
/// Macro Deck's own. Reading needs no permission.
/// <para>
/// Handlers of <see cref="Changed" /> subscribed through <see cref="IIntegrationContext.Batteries" /> are
/// released when Macro Deck shuts the integration down or initializes it again. Handlers run on a
/// background thread; one that throws is logged and does not affect the others.
/// </para>
/// </summary>
public interface IBatteryApi
{
	/// <summary>The current devices. Empty until Macro Deck has sent the first list.</summary>
	IReadOnlyList<BatteryDeviceSnapshot> Devices { get; }

	/// <summary>The device with the qualified <paramref name="id" />, or <c>null</c> when there is none.</summary>
	BatteryDeviceSnapshot? Find(string id);

	/// <summary>Raised after <see cref="Devices" /> changed.</summary>
	event EventHandler<BatteryDevicesChangedEventArgs>? Changed;
}
