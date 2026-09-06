using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;

namespace MacroDeckHost.Tests.UnitTests.MusicPlayer;

[TestFixture]
internal sealed class GetMusicPlayerInstancesRequestMessageHandlerTests
{
	private static readonly string[] _announcedInstanceIds = ["spotify::a"];

	[Test]
	public async Task The_pull_answers_from_the_announced_snapshot_not_the_live_registry()
	{
		// The registry is momentarily empty during an integration reinitialize while the broadcaster
		// deliberately holds the previous list. A pull during that gap used to hand the client an
		// explicit empty list that no later push corrected (issue: change-only broadcasting).
		var snapshot = new MusicPlayerInstancesSnapshot();
		snapshot.Record([new MusicPlayerInstanceDto { InstanceId = "spotify::a" }]);
		var handler = new GetMusicPlayerInstancesRequestMessageHandler(snapshot);

		var response = await handler.Handle(new GetMusicPlayerInstancesRequest(), CancellationToken.None);

		Assert.That(response.Instances.Select(i => i.InstanceId), Is.EqualTo(_announcedInstanceIds));
	}
}
