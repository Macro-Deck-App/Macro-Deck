using System.Text.Json;
using Json.Schema;
using MacroDeck.Localization;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Testing;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Application.StreamStats;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Integrations.Twitch;
using MacroDeckHost.Localization;
using MacroDeckHost.Tests.UnitTests.Twitch.Chat;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Widgets.Ui;
using MacroDeckHost.Widgets.StreamStats;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Twitch.Stats;

[TestFixture]
internal sealed class TwitchStatsWidgetConfigTests
{
	[Test]
	public void Every_style_is_offered_with_the_size_it_is_designed_for()
	{
		var host = Render(new { account = "" });
		var options = host.ById(StreamStatsWidgetType.StyleKey).Property(UiConfigProperties.Options)!.Value
			.EnumerateArray()
			.Select(option => (option.GetProperty("value").GetString(), option.GetProperty("label").GetRawText()))
			.ToList();

		var expected = new (string Value, LocalizedString Label, string English)[]
		{
			("overview", AppStrings.Integrations.StreamStats.Widget.StyleOverview(), "Overview (3x2)"),
			("statsRow", AppStrings.Integrations.StreamStats.Widget.StyleStatsRow(), "Stats row (3x1)"),
			("liveRow", AppStrings.Integrations.StreamStats.Widget.StyleLiveRow(), "Live status row (3x1)"),
			("valueGraph", AppStrings.Integrations.StreamStats.Widget.StyleValueGraph(),
				"Single value with graph (2x2)"),
			("value", AppStrings.Integrations.StreamStats.Widget.StyleValue(), "Single value (1x1)"),
		};

		Assert.Multiple(() =>
		{
			Assert.That(options.Select(option => option.Item1), Is.EqualTo(expected.Select(item => item.Value)));

			foreach (var (option, item) in options.Zip(expected))
			{
				Assert.That(option.Item2, Does.Contain(item.Label.Key.Name), item.Value);
				Assert.That(TestLocalization.Resolve(item.Label, "en"), Is.EqualTo(item.English));
			}
		});
	}

	[Test]
	public void Data_from_before_the_styles_existed_opens_on_the_overview_with_its_default_tiles()
	{
		var host = Render(new { account = "" });

		Assert.Multiple(() =>
		{
			Assert.That(host.ById(StreamStatsWidgetType.StyleKey).Text(UiConfigProperties.Value), Is.EqualTo("overview"));
			Assert.That(Strings(host.ById(StreamStatsWidgetType.TilesKey).Property(UiConfigProperties.Value)),
				Is.EqualTo(new[] { "viewers", "chatters", "followers" }));
		});
	}

	[TestCase("overview", false, true, true)]
	[TestCase("statsRow", false, true, false)]
	[TestCase("liveRow", true, false, false)]
	[TestCase("valueGraph", true, false, false)]
	[TestCase("value", true, false, false)]
	public void Each_style_offers_only_the_settings_it_uses(string style, bool metric, bool tiles, bool details)
	{
		var host = Render(new { account = "", style });

		Assert.Multiple(() =>
		{
			Assert.That(WidgetConfigTestSupport.IsVisible(host, StreamStatsWidgetType.MetricKey), Is.EqualTo(metric));
			Assert.That(WidgetConfigTestSupport.IsVisible(host, StreamStatsWidgetType.TilesKey), Is.EqualTo(tiles));
			Assert.That(WidgetConfigTestSupport.IsVisible(host, StreamStatsWidgetType.DetailsKey), Is.EqualTo(details));
			Assert.That(WidgetConfigTestSupport.IsVisible(host, StreamStatsWidgetType.ThumbnailKey), Is.EqualTo(details));
		});
	}

	[Test]
	public void Tiles_and_details_are_orderable_lists()
		=> Assert.Multiple(() =>
		{
			var host = Render(new { account = "" });

			Assert.That(host.ById(StreamStatsWidgetType.TilesKey).Flag(UiConfigProperties.Reorderable), Is.True);
			Assert.That(host.ById(StreamStatsWidgetType.DetailsKey).Flag(UiConfigProperties.Reorderable), Is.True);
		});

	[Test]
	public async Task The_default_data_and_every_edit_validate_against_the_widget_schema()
	{
		var config = new RecordingIntegrationConfig();
		TwitchChatTestSupport.AddAccount(config, "111", "streamer");
		using var manager = new TwitchAccountManager(() => new FakeTwitchOAuthClient(),
			Logger.None,
			(_, _) => new FakeTwitchHelixClient());
		using var integration = new TwitchIntegration(manager);
		await manager.ReloadAsync(config);
		var descriptor = integration.GetWidgetTypes().Single(type => type.Id == StreamStatsWidgetType.LocalId);
		var schema = JsonSchema.FromText(descriptor.DataSchema!);
		var defaults = JsonDocument.Parse(descriptor.DefaultData!).RootElement;

		var host = Render(defaults);
		host.ById(StreamStatsWidgetType.StyleKey).Change("value");
		host.ById(StreamStatsWidgetType.MetricKey).Change("subscribers");
		host.ById(StreamStatsWidgetType.TilesKey).Change(new[] { "subscribers", "viewers" });
		host.ById(StreamStatsWidgetType.DetailsKey).Change(new[] { "uptime" });
		host.ById(StreamStatsWidgetType.ThumbnailKey).Change(false);

		var edited = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
		{
			["account"] = "",
			["style"] = host.ById(StreamStatsWidgetType.StyleKey).Text(UiConfigProperties.Value),
			["metric"] = host.ById(StreamStatsWidgetType.MetricKey).Text(UiConfigProperties.Value),
			["tiles"] = host.ById(StreamStatsWidgetType.TilesKey).Property(UiConfigProperties.Value),
			["details"] = host.ById(StreamStatsWidgetType.DetailsKey).Property(UiConfigProperties.Value),
			["showThumbnail"] = host.ById(StreamStatsWidgetType.ThumbnailKey).Flag(UiConfigProperties.Value),
		});

		Assert.Multiple(() =>
		{
			Assert.That(WidgetDataSchema.Validate(schema, defaults), Is.Empty);
			Assert.That(WidgetDataSchema.Validate(schema, edited), Is.Empty);
			Assert.That(Strings(edited.GetProperty("tiles")), Is.EqualTo(new[] { "subscribers", "viewers" }));
			Assert.That(StreamStatsWidgetSettings.Options(StreamPlatforms.Twitch, edited).Metric,
				Is.EqualTo("subscribers"));
		});
	}

	private static UiTestHost Render(object data)
		=> UiTestHost.Render(StreamStatsWidgetConfigView.Build(StreamPlatforms.Twitch,
			data is JsonElement element ? element : JsonSerializer.SerializeToElement(data),
			[new StreamStatsAccount("111", "Streamer", "twitch_streamer_")]));

	private static string[] Strings(JsonElement? list)
		=> [.. list!.Value.EnumerateArray().Select(item => item.GetString()!)];
}
