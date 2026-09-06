using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Localization;

namespace MacroDeckHost.Application.MusicPlayer;

public sealed record MusicPlayerInstanceDescriptor(
	string InstanceId,
	string IntegrationId,
	LocalizedText ProviderName,
	string DisplayName,
	bool HasIcon);

public interface IMusicPlayerRegistry
{
	IReadOnlyList<MusicPlayerInstanceDescriptor> GetInstances();

	IMusicPlayer? GetPlayer(string instanceId);

	IMusicPlayer? DefaultPlayer { get; }
}
