namespace MacroDeck.Sdk.ConfigFlow;

/// <summary>A persisted config entry as seen by an integration at runtime.</summary>
public sealed record ConfigEntrySnapshot(Guid Id, string Title);
