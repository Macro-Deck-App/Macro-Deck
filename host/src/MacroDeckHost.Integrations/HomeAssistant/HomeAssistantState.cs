namespace MacroDeckHost.Integrations.HomeAssistant;

internal sealed record HomeAssistantState
{
	public static HomeAssistantState Disconnected { get; } = new();

	public bool IsConnected { get; init; }

	public string? Version { get; init; }

	public string? LocationName { get; init; }

	public int? EntityCount { get; init; }
}
