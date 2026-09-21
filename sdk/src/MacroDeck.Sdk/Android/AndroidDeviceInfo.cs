namespace MacroDeck.Sdk.Android;

/// <summary>What the device reports about itself. Any value can be <c>null</c> until the device is online.</summary>
public sealed record AndroidDeviceInfo(string? Model, string? Manufacturer, string? Product);
