namespace MacroDeck.Sdk.Devices;

/// <summary>
/// What the host assigned to a registered device. <paramref name="DeviceId" /> is the host's global id -
/// stable for as long as the device exists in Macro Deck, including across reconnects and restarts - and
/// <paramref name="ProviderDeviceId" /> is the provider-local id it was registered under.
/// </summary>
public sealed record DeviceRegistration(string DeviceId, string ProviderDeviceId);
