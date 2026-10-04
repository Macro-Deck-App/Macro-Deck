using MacroDeck.Localization;
using MacroDeckHost.Application.Twitch.Chat;
using MacroDeckHost.Widgets.Preview;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Twitch.ChatWidget;

namespace MacroDeckHost.Widgets.TwitchChat;

internal static class TwitchChatWidgetSample
{
	private static readonly (Func<LocalizedString> Chatter, Func<LocalizedString> Message)[] _lines =
	[
		(Strings.SampleChatter1, Strings.SampleMessage1),
		(Strings.SampleChatter2, Strings.SampleMessage2),
		(Strings.SampleChatter3, Strings.SampleMessage3),
	];

	public static async Task<TwitchChatViewState> BuildAsync(
		IWidgetSampleTextResolver text,
		string separator,
		TwitchChatLineLayout? layout = null)
	{
		ArgumentNullException.ThrowIfNull(text);

		var lines = new List<TwitchChatLine>();

		for (var index = 0; index < _lines.Length; index++)
		{
			var chatter = await text.ResolveAsync(_lines[index].Chatter()).ConfigureAwait(false);
			var message = await text.ResolveAsync(_lines[index].Message()).ConfigureAwait(false);
			var id = "sample-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

			lines.Add(TwitchChatLines.Build(new TwitchChatMessage(id,
					id,
					chatter,
					chatter,
					TwitchChatStyle.DefaultColor(chatter),
					[],
					[new TwitchChatFragment(TwitchChatFragmentKind.Text, message)]),
				separator,
				images: null,
				layout));
		}

		return new TwitchChatViewState(TwitchChatStatus.Messages, lines, lines);
	}
}
