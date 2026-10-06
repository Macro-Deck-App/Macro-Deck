using MacroDeck.Localization;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Widgets.Preview;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.StreamChat.Widget;

namespace MacroDeckHost.Widgets.StreamChat;

internal static class StreamChatWidgetSample
{
	private static readonly (Func<LocalizedString> Chatter, Func<LocalizedString> Message)[] _lines =
	[
		(Strings.SampleChatter1, Strings.SampleMessage1),
		(Strings.SampleChatter2, Strings.SampleMessage2),
		(Strings.SampleChatter3, Strings.SampleMessage3),
	];

	public static async Task<StreamChatViewState> BuildAsync(
		IWidgetSampleTextResolver text,
		string separator,
		StreamChatLineLayout? layout = null)
	{
		ArgumentNullException.ThrowIfNull(text);

		var lines = new List<StreamChatLine>();

		for (var index = 0; index < _lines.Length; index++)
		{
			var chatter = await text.ResolveAsync(_lines[index].Chatter()).ConfigureAwait(false);
			var message = await text.ResolveAsync(_lines[index].Message()).ConfigureAwait(false);
			var id = "sample-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

			lines.Add(StreamChatLines.Build(new ChatMessage(id,
					id,
					chatter,
					chatter,
					ChatStyle.DefaultColor(chatter),
					[],
					[new ChatFragment(ChatFragmentKind.Text, message)]),
				separator,
				images: null,
				layout));
		}

		return new StreamChatViewState(StreamChatStatus.Messages, lines, lines);
	}
}
