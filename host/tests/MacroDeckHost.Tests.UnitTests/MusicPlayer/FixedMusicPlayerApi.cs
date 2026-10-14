using MacroDeck.Sdk;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeckHost.Infrastructure;

namespace MacroDeckHost.Tests.UnitTests.MusicPlayer;

internal sealed class FixedMusicPlayerApi : IMusicPlayerApi
{
	public List<MusicPlayerInstanceSnapshot> Players { get; } = [];

	public Dictionary<string, string> Colors { get; } = new(StringComparer.Ordinal);

	public IReadOnlyList<MusicPlayerInstanceSnapshot> Instances => [.. Players];

	public event EventHandler? InstancesChanged
	{
		add { }
		remove { }
	}

	public Task<string?> GetAlbumColorAsync(string instanceId, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(Colors.GetValueOrDefault(instanceId));
	}

	public static IIntegrationContext ContextWith(IMusicPlayerApi musicPlayers)
	{
		return new IntegrationContext(null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			musicPlayers: musicPlayers);
	}
}
