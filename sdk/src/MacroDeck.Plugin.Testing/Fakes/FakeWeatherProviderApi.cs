using MacroDeck.Sdk.Weather;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// Records what a weather provider tells Macro Deck about its stations. <see cref="PluginTestHarness" />
/// hands it to the plugin's integrations, and <see cref="MacroDeckTestHost" /> records the
/// <c>weather</c> notices it accepts over the wire here.
/// </summary>
public sealed class FakeWeatherProviderApi : IWeatherProviderApi
{
	private int _snapshotNotices;

	/// <summary>How many times the provider reported changed station snapshots through
	/// <see cref="NotifySnapshotsChangedAsync" />.</summary>
	public int SnapshotNotices => Volatile.Read(ref _snapshotNotices);

	/// <inheritdoc />
	public Task NotifySnapshotsChangedAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Interlocked.Increment(ref _snapshotNotices);
		return Task.CompletedTask;
	}
}
