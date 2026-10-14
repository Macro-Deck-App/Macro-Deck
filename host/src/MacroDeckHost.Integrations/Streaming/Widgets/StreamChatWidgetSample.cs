using System.Globalization;
using MacroDeck.Localization;
using MacroDeckHost.Integrations.Ui;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.StreamChat.Widget;

namespace MacroDeckHost.Integrations.Streaming.Widgets;

internal static class StreamChatWidgetSample
{
	private static readonly (Func<LocalizedString> Chatter, Func<LocalizedString> Message)[] _lines =
	[
		(Strings.SampleChatter1, Strings.SampleMessage1),
		(Strings.SampleChatter2, Strings.SampleMessage2),
		(Strings.SampleChatter3, Strings.SampleMessage3)
	];

	public static StreamChatViewState Build(string? culture, string separator, StreamChatLineLayout? layout = null)
	{
		var lines = new List<StreamChatLine>();

		for (var index = 0; index < _lines.Length; index++)
		{
			var chatter = WidgetText.Resolve(_lines[index].Chatter(), culture);
			var message = WidgetText.Resolve(_lines[index].Message(), culture);
			var id = "sample-" + index.ToString(CultureInfo.InvariantCulture);

			lines.Add(StreamChatLines.Build(new ChatMessage(id,
					id,
					chatter,
					chatter,
					ChatStyle.DefaultColor(chatter),
					[],
					[new ChatFragment(ChatFragmentKind.Text, message)]),
				separator,
				null,
				layout));
		}

		return new StreamChatViewState(StreamChatStatus.Messages, lines, lines);
	}
}
