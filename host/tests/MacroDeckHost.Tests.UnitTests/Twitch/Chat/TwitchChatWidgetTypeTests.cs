using MacroDeckHost.Application.Twitch.Chat;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Integrations.Twitch;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Twitch.Chat;

[TestFixture]
internal sealed class TwitchChatWidgetTypeTests
{
	private RecordingIntegrationConfig _config = null!;
	private TwitchAccountManager _manager = null!;
	private TwitchIntegration _integration = null!;
	private WidgetTypeRegistry _registry = null!;
	private WidgetTypeProviderHost _host = null!;

	[SetUp]
	public void SetUp()
	{
		_config = new RecordingIntegrationConfig();
		_manager = new TwitchAccountManager(() => new FakeTwitchOAuthClient(),
			Logger.None,
			(_, _) => new FakeTwitchHelixClient());
		_integration = new TwitchIntegration(_manager);
		_registry = new WidgetTypeRegistry(new RecordingMediator());
		_host = new WidgetTypeProviderHost(_registry, TimeProvider.System, Logger.None);
	}

	[TearDown]
	public void TearDown()
	{
		_integration.Dispose();
		_manager.Dispose();
	}

	[Test]
	public async Task Without_a_twitch_account_there_is_no_chat_widget_to_pick()
	{
		await _manager.ReloadAsync(_config);
		await _host.StartAsync(_integration);

		Assert.That(_registry.IsRegistered(TwitchChatWidgetType.QualifiedId), Is.False);
	}

	[Test]
	public async Task A_connected_account_offers_a_configurable_chat_widget()
	{
		TwitchChatTestSupport.AddAccount(_config, "111", "streamer");
		await _manager.ReloadAsync(_config);
		await _host.StartAsync(_integration);

		Assert.Multiple(() =>
		{
			Assert.That(_registry.TryResolve(TwitchChatWidgetType.QualifiedId, out var entry), Is.True);
			Assert.That(entry.ProviderId, Is.EqualTo(TwitchIntegration.IntegrationId));
			Assert.That(entry.Descriptor.HasConfiguration, Is.True);
			Assert.That(TestLocalization.Resolve(entry.Descriptor.Name), Is.EqualTo("Twitch Chat"));
		});
	}

	[Test]
	public async Task The_chat_widget_data_schema_offers_a_font_size_between_25_and_300_percent()
	{
		TwitchChatTestSupport.AddAccount(_config, "111", "streamer");
		await _manager.ReloadAsync(_config);
		await _host.StartAsync(_integration);

		Assert.That(_registry.TryResolve(TwitchChatWidgetType.QualifiedId, out var entry), Is.True);

		using var schema = global::System.Text.Json.JsonDocument.Parse(entry.Descriptor.DataSchema!);
		var fontSize = schema.RootElement.GetProperty("properties").GetProperty("textSize");

		Assert.Multiple(() =>
		{
			Assert.That(fontSize.GetProperty("minimum").GetDouble(), Is.EqualTo(25));
			Assert.That(fontSize.GetProperty("maximum").GetDouble(), Is.EqualTo(300));
		});
	}

	[Test]
	public async Task The_chat_widget_data_schema_offers_optional_message_and_name_colours()
	{
		TwitchChatTestSupport.AddAccount(_config, "111", "streamer");
		await _manager.ReloadAsync(_config);
		await _host.StartAsync(_integration);

		Assert.That(_registry.TryResolve(TwitchChatWidgetType.QualifiedId, out var entry), Is.True);

		using var schema = global::System.Text.Json.JsonDocument.Parse(entry.Descriptor.DataSchema!);
		var properties = schema.RootElement.GetProperty("properties");

		Assert.Multiple(() =>
		{
			foreach (var key in new[] { "messageColor", "nameColor" })
			{
				var types = properties.GetProperty(key).GetProperty("type").EnumerateArray()
					.Select(type => type.GetString()).ToList();
				Assert.That(types, Is.EquivalentTo(new[] { "string", "null" }), key);
			}
		});
	}

	[Test]
	public async Task Removing_the_last_account_withdraws_the_chat_widget_on_reinitialization()
	{
		TwitchChatTestSupport.AddAccount(_config, "111", "streamer");
		await _manager.ReloadAsync(_config);
		await _host.StartAsync(_integration);

		_config.Entries.Clear();
		await _host.StopAsync(_integration);
		await _manager.ReloadAsync(_config);
		await _host.StartAsync(_integration);

		Assert.That(_registry.IsRegistered(TwitchChatWidgetType.QualifiedId), Is.False);
	}

	[Test]
	public async Task Shutting_down_tells_the_chat_that_no_account_is_left()
	{
		var sink = new RecordingTwitchChatSink();
		_integration.UseTwitchChatSink(sink);

		await _integration.ShutdownAsync();

		Assert.That(sink.AccountLists, Has.Count.EqualTo(1).And.All.Empty);
	}
}
