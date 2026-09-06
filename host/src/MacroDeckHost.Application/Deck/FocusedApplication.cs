namespace MacroDeckHost.Application.Deck;

public sealed record FocusedApplication(int ProcessId, string? ExecutablePath, string? ProcessName, string? BundleId);
