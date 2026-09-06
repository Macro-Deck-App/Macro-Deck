using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;

namespace MacroDeckHost.Tests.UnitTests.MusicPlayer;

[TestFixture]
internal sealed class MusicPlayerClientSyncTests
{
	[Test]
	public void The_handshake_sends_instances_first_then_every_cached_state()
	{
		var snapshot = new MusicPlayerInstancesSnapshot();
		snapshot.Record([new MusicPlayerInstanceDto { InstanceId = "spotify::a" }]);
		var cache = new MusicPlayerStateCache();
		cache.Record("spotify::a", new MusicPlayerStatePayload { InstanceId = "spotify::a", IsConnected = true });
		var sync = new MusicPlayerClientSync(snapshot, cache);

		var handshake = sync.BuildHandshake();

		Assert.Multiple(() =>
		{
			Assert.That(handshake, Has.Count.EqualTo(2));
			Assert.That(handshake[0].Name, Is.EqualTo("MusicPlayerInstancesChangedNotification"));
			Assert.That(handshake[1].Name, Is.EqualTo("MusicPlayerStateChangedNotification"));
			Assert.That(((MusicPlayerStateChangedNotification)handshake[1].Payload).State.IsConnected, Is.True);
		});
	}

	[Test]
	public void An_empty_cache_yields_only_the_instance_list()
	{
		var sync = new MusicPlayerClientSync(new MusicPlayerInstancesSnapshot(), new MusicPlayerStateCache());

		var handshake = sync.BuildHandshake();

		Assert.Multiple(() =>
		{
			Assert.That(handshake, Has.Count.EqualTo(1));
			Assert.That(handshake[0].Name, Is.EqualTo("MusicPlayerInstancesChangedNotification"));
		});
	}
}
