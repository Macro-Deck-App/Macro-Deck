using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Application.StreamStats;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Streaming;

internal static class StreamPlatformTestSupport
{
	public static StreamPlatform TestPlatform { get; } = new()
	{
		OwnerId = "app.macro-deck.test-platform",
		DialogViewId = "test-platform-chat",
		AccentColor = "#ff0000",
		AccountLabel = StreamPlatforms.Twitch.AccountLabel,
		Chat = StreamPlatforms.Twitch.Chat,
		Stats = StreamPlatforms.Twitch.Stats,
	};

	public static StreamPlatformServiceSet Set(
		StreamPlatform platform,
		IStreamChatSink? chatSink = null,
		IStreamStatsSink? statsSink = null)
	{
		var chat = new StreamChatHub(TimeProvider.System, Logger.None);
		var stats = new StreamStatsAccountsHub();

		return new StreamPlatformServiceSet(platform,
			chatSink ?? chat,
			chat,
			statsSink ?? stats,
			stats,
			new NoStreamThumbnails());
	}

	public static StreamPlatformServices Services(params StreamPlatformServiceSet[] sets) => new(sets);
}

internal sealed class NoStreamThumbnails : IStreamThumbnails
{
	public event EventHandler<string>? Changed
	{
		add { }
		remove { }
	}

	public UiResource? Find(string accountId) => null;

	public void Track(string accountId, string? url)
	{
	}
}
