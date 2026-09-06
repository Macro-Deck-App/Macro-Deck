namespace MacroDeck.Sdk.Devices;

/// <summary>The profile a device's current surface belongs to.</summary>
/// <param name="Id">The profile's id.</param>
/// <param name="Name">The profile's name.</param>
public sealed record DeviceSurfaceProfile(string Id, string Name);
