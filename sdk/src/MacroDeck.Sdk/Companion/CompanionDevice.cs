namespace MacroDeck.Sdk.Companion;

/// <summary>A connected Companion device and what it last reported.</summary>
/// <param name="Id">The device's id in Macro Deck, the same id its deck client and its Companion configuration use.</param>
/// <param name="ReadySequence">
/// Changes every time the device becomes ready again after it was gone, and only then: two
/// <see cref="ICompanionDeviceApi.DeviceReady" /> events with the same value describe the same connection.
/// </param>
/// <param name="State">The device's last report.</param>
public sealed record CompanionDevice(Guid Id, long ReadySequence, CompanionDeviceState State);
