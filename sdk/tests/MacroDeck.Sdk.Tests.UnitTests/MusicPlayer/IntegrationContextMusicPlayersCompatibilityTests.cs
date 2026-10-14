using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Sdk.Tests.UnitTests.MusicPlayer;

[TestFixture]
public class IntegrationContextMusicPlayersCompatibilityTests
{
	[Test]
	public async Task A_context_written_before_music_players_existed_still_builds_and_reports_no_players()
	{
		IIntegrationContext context = new ContextWrittenBeforeMusicPlayers();
		var raised = false;

		context.MusicPlayers.InstancesChanged += (_, _) => raised = true;

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(context.MusicPlayers.Instances, Is.Empty);
			Assert.That(await context.MusicPlayers.GetAlbumColorAsync("spotify::default"), Is.Null);
			Assert.That(raised, Is.False);
		});
	}

	[Test]
	public void The_unsupported_api_still_rejects_an_empty_instance_id()
	{
		Assert.ThrowsAsync<ArgumentException>(()
			=> UnsupportedMusicPlayerApi.Instance.GetAlbumColorAsync(string.Empty));
	}

	[Test]
	public void A_snapshot_written_with_the_original_members_still_builds()
	{
		var snapshot = new MusicPlayerInstanceSnapshot { Id = "a::b", ProviderId = "a", DisplayName = "B" };

		Assert.Multiple(() =>
		{
			Assert.That(snapshot.ProviderName.IsEmpty, Is.True);
			Assert.That(snapshot with { DisplayName = "C" }, Is.Not.EqualTo(snapshot));
		});
	}

	private sealed class ContextWrittenBeforeMusicPlayers : IIntegrationContext
	{
		public IVariableApi Variables => throw new NotSupportedException();

		public IUserVariableApi UserVariables => throw new NotSupportedException();

		public IIntegrationConfig Config => throw new NotSupportedException();

		public IDeckNavigator Deck => throw new NotSupportedException();

		public IScriptApi Scripts => throw new NotSupportedException();

		public IWidgetApi Widgets => throw new NotSupportedException();

		public IEventPublisher Events => throw new NotSupportedException();

		public IUserNotifier Notifications => throw new NotSupportedException();
	}
}
