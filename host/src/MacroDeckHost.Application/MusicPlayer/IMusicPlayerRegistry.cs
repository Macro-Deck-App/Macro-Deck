using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeckHost.Application.MusicPlayer;

public sealed record MusicPlayerInstanceDescriptor(
	string InstanceId,
	string IntegrationId,
	LocalizedText ProviderName,
	string DisplayName,
	bool HasIcon)
{
	public IReadOnlyList<ActionParameter> Options { get; init; } = [];
}

public interface IMusicPlayerRegistry
{
	IReadOnlyList<MusicPlayerInstanceDescriptor> GetInstances();

	IMusicPlayer? GetPlayer(string instanceId);

	IMusicPlayer? GetPlayerWithOptions(string instanceId, IReadOnlyDictionary<string, object> options);

	IMusicPlayer? DefaultPlayer { get; }
}
