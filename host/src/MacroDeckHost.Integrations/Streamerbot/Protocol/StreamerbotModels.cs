using System.Text.Json;

namespace MacroDeckHost.Integrations.Streamerbot.Protocol;

internal sealed record StreamerbotHello(
	StreamerbotInstanceInfo? Info,
	StreamerbotChallenge? Authentication);

internal sealed record StreamerbotChallenge(string Salt, string Challenge);

internal sealed record StreamerbotInstanceInfo(string? Name, string? Version, string? InstanceId, string? Os);

internal sealed record StreamerbotActionInfo(string Id, string Name, string? Group, bool Enabled);

internal sealed record StreamerbotCodeTrigger(string Name, string? Category);

internal sealed record StreamerbotBroadcaster(string? Platform, string? UserName);

internal sealed record StreamerbotEventMessage(string Source, string Type, JsonElement Data);
