using MacroDeckHost.Integrations.Spotify;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer.Actions;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

[TestFixture]
internal sealed class SpotifyIntegrationInstanceGuardTests
{
	[Test]
	public void ReadAsync_WithoutInstances_ReportsDisconnectedInsteadOfThrowing()
	{
		var integration = new SpotifyIntegration();

		Assert.Multiple(() =>
		{
			foreach (var variable in integration.Variables)
			{
				Assert.DoesNotThrowAsync(
					async () => await integration.ReadAsync(variable.ResolvedId!, CancellationToken.None),
					$"reading '{variable.Name}' must not throw while Spotify has no instances");
			}
		});
	}

	[Test]
	public async Task ReadAsync_WithoutInstances_ReportsNotConnected()
	{
		var integration = new SpotifyIntegration();

		var isConnected = (await integration.ReadAsync("spotify-is-connected", CancellationToken.None)).Value;

		Assert.That(isConnected, Is.False);
	}

	[Test]
	public void Actions_WithoutInstances_NoOpInsteadOfThrowing()
	{
		var integration = new SpotifyIntegration();
		var context = new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>
			{
				[MusicPlayerActions.InstanceParameterName] = "some-entry-id"
			}
		};

		Assert.Multiple(() =>
		{
			foreach (var action in integration.Actions)
			{
				Assert.DoesNotThrowAsync(() => action.CreateExecutor().ExecuteAsync(context),
					$"executing '{action.Id}' must not throw while Spotify has no instances");
			}
		});
	}
}
