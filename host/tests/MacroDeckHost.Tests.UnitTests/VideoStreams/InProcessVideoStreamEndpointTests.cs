using MacroDeck.Sdk.VideoStreams;
using MacroDeckHost.Application.VideoStreams;

namespace MacroDeckHost.Tests.UnitTests.VideoStreams;

[TestFixture]
public class InProcessVideoStreamEndpointTests
{
	private const int Rounds = 200;

	[Test]
	public async Task A_close_racing_a_removal_closes_the_original_provider_once_and_never_its_replacement()
	{
		for (var round = 0; round < Rounds; round++)
		{
			var endpoint = new InProcessVideoStreamEndpoint(TimeProvider.System,
				TimeSpan.FromSeconds(5),
				Serilog.Core.Logger.None);
			var original = new ScriptedVideoProvider("cam", "main");
			var replacement = new ScriptedVideoProvider("cam", "main");
			endpoint.Add(original);
			var sessionId = "s" + round;
			await endpoint.OpenAsync("cam",
				new VideoStreamOpenRequest(sessionId,
					"main",
					["hls"],
					new VideoStreamConsumer(null, null, VideoStreamConnectionKind.Local)),
				() => true,
				CancellationToken.None);

			using var start = new Barrier(2);
			var close = Task.Run(async () =>
			{
				start.SignalAndWait();
				await endpoint.CloseAsync("cam", sessionId, VideoStreamSessionReason.ConsumerClosed, CancellationToken.None);
			});
			var replace = Task.Run(async () =>
			{
				start.SignalAndWait();
				await endpoint.RemoveAsync("cam");
				endpoint.Add(replacement);
			});
			await Task.WhenAll(close, replace);

			Assert.Multiple(() =>
			{
				Assert.That(original.Closes.Select(closed => closed.SessionId), Is.EqualTo(new[] { sessionId }),
					$"round {round}");
				Assert.That(replacement.Closes, Is.Empty, $"round {round}");
			});
		}
	}
}
