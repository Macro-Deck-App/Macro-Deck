using MacroDeckHost.Integrations.SoundPad;

namespace MacroDeckHost.Tests.UnitTests.SoundPad;

internal static class SoundPadTestSupport
{
	public static async Task<SoundPadConnection> ConnectedAsync(FakeSoundPadClient client)
	{
		var connection = new SoundPadConnection(() => client, TimeSpan.FromMilliseconds(20));
		connection.Start();
		await WaitUntilAsync(() => connection.IsConnected);
		return connection;
	}

	public static async Task WaitUntilAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail("The condition was not met within 5 seconds.");
			}

			await Task.Delay(10);
		}
	}

	public static SoundPadSound Sound(int index, string title, string? path = null, DateTime? lastPlayedOn = null,
		string? artist = null)
		=> new(index, title, artist, path ?? $@"C:\Sounds\{title}.mp3", TimeSpan.FromSeconds(3), lastPlayedOn);
}
