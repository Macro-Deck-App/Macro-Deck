namespace MacroDeck.Sdk.Devices;

/// <summary>The folder a device's current surface renders.</summary>
/// <param name="Id">The folder's id.</param>
/// <param name="Name">The folder's name.</param>
/// <param name="ParentId">The parent folder's id, or <c>null</c> when <paramref name="IsRoot" /> is true.</param>
/// <param name="IsRoot">Whether this is the profile's root folder.</param>
public sealed record DeviceSurfaceFolder(string Id, string Name, string? ParentId, bool IsRoot);
