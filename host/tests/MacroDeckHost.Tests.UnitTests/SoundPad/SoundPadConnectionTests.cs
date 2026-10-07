using MacroDeckHost.Integrations.SoundPad;

namespace MacroDeckHost.Tests.UnitTests.SoundPad;

[TestFixture]
public class SoundPadConnectionTests
{
	[Test]
	public async Task ConnectsOnceSoundPadStarts()
	{
		var client = new FakeSoundPadClient { Reachable = false };
		using var connection = new SoundPadConnection(() => client, TimeSpan.FromMilliseconds(20));
		connection.Start();

		await SoundPadTestSupport.WaitUntilAsync(() => client.ConnectAttempts >= 2);
		Assert.That(connection.IsConnected, Is.False);

		client.Reachable = true;

		await SoundPadTestSupport.WaitUntilAsync(() => connection.IsConnected);
	}

	[Test]
	public async Task AStuckCall_TimesOut_AndTheConnectionRecovers()
	{
		var client = new FakeSoundPadClient();
		using var connection = new SoundPadConnection(() => client,
			TimeSpan.FromMilliseconds(20),
			TimeSpan.FromMilliseconds(100));
		connection.Start();
		await SoundPadTestSupport.WaitUntilAsync(() => connection.IsConnected);

		client.HangCalls = true;
		Assert.ThrowsAsync<SoundPadUnavailableException>(() =>
			connection.RunAsync(c => c.GetPlayStatusAsync(), CancellationToken.None));

		client.HangCalls = false;
		await SoundPadTestSupport.WaitUntilAsync(() => connection.IsConnected);
		var status = await connection.RunAsync(c => c.GetPlayStatusAsync(), CancellationToken.None);

		Assert.That(status, Is.EqualTo(SoundPadPlayStatus.Stopped));
	}

	[Test]
	public void ACommandWhileSoundPadIsClosed_FailsAsUnavailable()
	{
		var client = new FakeSoundPadClient { Reachable = false };
		using var connection = new SoundPadConnection(() => client, TimeSpan.FromMinutes(1));
		connection.Start();

		Assert.ThrowsAsync<SoundPadUnavailableException>(() =>
			connection.RunAsync(c => c.StopSoundAsync(), CancellationToken.None, connectOnDemand: true));
	}

	[Test]
	public async Task ACommandRightAfterSoundPadStarts_ConnectsWithoutWaitingForTheRetry()
	{
		var client = new FakeSoundPadClient { Reachable = false };
		using var connection = new SoundPadConnection(() => client, TimeSpan.FromMinutes(1));
		connection.Start();
		await SoundPadTestSupport.WaitUntilAsync(() => client.ConnectAttempts >= 1);

		client.Reachable = true;
		await connection.RunAsync(c => c.StopSoundAsync(), CancellationToken.None, connectOnDemand: true);

		Assert.That(client.Commands, Is.EqualTo(new[] { "stop" }));
	}

	[Test]
	public async Task Dispose_StopsReconnecting_AndReleasesThePipe()
	{
		var client = new FakeSoundPadClient { Reachable = false };
		var connection = new SoundPadConnection(() => client, TimeSpan.FromMilliseconds(20));
		connection.Start();
		await SoundPadTestSupport.WaitUntilAsync(() => client.ConnectAttempts >= 1);

		connection.Dispose();
		var attempts = client.ConnectAttempts;
		await Task.Delay(150);

		Assert.Multiple(() =>
		{
			Assert.That(client.Disposed, Is.True);
			Assert.That(client.ConnectAttempts, Is.LessThanOrEqualTo(attempts + 1));
		});
	}
}
