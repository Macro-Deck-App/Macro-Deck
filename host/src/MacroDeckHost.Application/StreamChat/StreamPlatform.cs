using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.StreamStats;

namespace MacroDeckHost.Application.StreamChat;

public sealed class StreamChatTexts
{
	public required Func<LocalizedString> Name { get; init; }

	public required Func<LocalizedString> Description { get; init; }

	public required Func<LocalizedString> Heading { get; init; }

	public required Func<LocalizedString> AccountDescription { get; init; }

	public required Func<LocalizedString> Offline { get; init; }

	public required Func<string, LocalizedString> Title { get; init; }

	public required Func<string, LocalizedString> SharedChat { get; init; }

	public required Func<LocalizedString> AccountChanged { get; init; }

	public required Func<LocalizedString> AccountUnavailable { get; init; }

	public required Func<LocalizedString> NotPermitted { get; init; }

	public required Func<LocalizedString> Refused { get; init; }

	public required Func<LocalizedString> MissingScope { get; init; }

	public Func<LocalizedString>? Unsupported { get; init; }
}

public sealed class StreamPlatform
{
	public const string ChatDefaultData = """{"account":"","allowModeration":true}""";

	public const string ChatDataSchema
		= """{"type":"object","properties":{"account":{"type":"string"},"allowModeration":{"type":"boolean"},"backgroundColor":{"type":["string","null"],"description":"#rrggbb or transparent"},"textSize":{"type":["number","null"],"minimum":25,"maximum":300,"description":"Chat text size in percent, 100 when unset"}}}""";

	public required string OwnerId { get; init; }

	public required string DialogViewId { get; init; }

	public required string AccentColor { get; init; }

	public required Func<LocalizedString> AccountLabel { get; init; }

	public required StreamChatTexts Chat { get; init; }

	public required StreamStatsDescriptor Stats { get; init; }

	public StreamThumbnailRule? Thumbnails { get; init; }

	public string ChatWidgetTypeId => StreamChatWidgetType.QualifiedId(OwnerId);

	public string StatsWidgetTypeId => StreamStatsWidgetType.QualifiedId(OwnerId);

	public string StatsDefaultData
		=> $$"""{"account":"","style":"{{StreamStatsWidgetType.DefaultStyle}}","metric":"{{Stats.DefaultMetric}}","tiles":{{Json(Stats.DefaultTiles)}},"details":{{Json(Stats.DetailIds)}},"showThumbnail":true}""";

	public string StatsDataSchema
		=> """{"type":"object","properties":{"account":{"type":"string"},"style":{"type":"string","enum":""" +
			Json(StreamStatsWidgetType.Styles) +
			"""},"metric":{"type":"string","enum":""" + Json(Stats.MetricIds) +
			"""},"tiles":{"type":"array","items":{"type":"string","enum":""" + Json(Stats.MetricIds) +
			"""}},"details":{"type":"array","items":{"type":"string","enum":""" + Json(Stats.DetailIds) +
			"""}},"showThumbnail":{"type":"boolean"},"backgroundColor":{"type":["string","null"],"description":"#rrggbb or transparent"}}}""";

	public WidgetTypeDescriptor ChatWidgetDescriptor()
		=> new(StreamChatWidgetType.LocalId,
			Chat.Name(),
			Chat.Description(),
			DefaultData: ChatDefaultData,
			DataSchema: ChatDataSchema,
			HasConfiguration: true)
		{
			AppearanceProperties = [WidgetAppearanceProperty.BackgroundColor],
		};

	public WidgetTypeDescriptor StatsWidgetDescriptor()
		=> new(StreamStatsWidgetType.LocalId,
			Stats.Name(),
			Stats.Description(),
			DefaultData: StatsDefaultData,
			DataSchema: StatsDataSchema,
			HasConfiguration: true)
		{
			AppearanceProperties = [WidgetAppearanceProperty.BackgroundColor],
		};

	private static string Json(IReadOnlyList<string> values) => JsonSerializer.Serialize(values);
}
