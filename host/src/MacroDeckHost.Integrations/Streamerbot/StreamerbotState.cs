using MacroDeckHost.Integrations.Streamerbot.Protocol;

namespace MacroDeckHost.Integrations.Streamerbot;

internal sealed record StreamerbotState
{
	public static StreamerbotState Disconnected { get; } = new();

	public bool IsConnected { get; init; }

	public string? InstanceName { get; init; }

	public string? Version { get; init; }

	public string? BroadcasterName { get; init; }

	public string? BroadcasterPlatform { get; init; }
}

internal sealed record StreamerbotCatalog
{
	public static StreamerbotCatalog Empty { get; } = new();

	public IReadOnlyList<StreamerbotActionInfo> Actions { get; init; } = [];

	public IReadOnlyList<StreamerbotCodeTrigger> CodeTriggers { get; init; } = [];

	public IReadOnlyDictionary<string, IReadOnlyList<string>> Events { get; init; }
		= StreamerbotResponses.EmptyEventCatalog;
}
