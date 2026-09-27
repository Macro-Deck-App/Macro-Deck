using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;
using MacroDeckHost.Application.Plugins.IconPacks;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Plugins.IconPacks;

[TestFixture]
internal sealed class PluginIconTranslationTests
{
	private const string PluginId = "com.example.logos";
	private const string OtherPluginId = "com.example.other";

	private PluginIconPackTestHost _host = null!;
	private Guid _spotifyId;

	[SetUp]
	public async Task SetUp()
	{
		_host = new PluginIconPackTestHost();
		await _host.SyncDevelopment(PluginId, ("logos", await _host.BuildArchive("Logos", ("spotify", "green"))));
		_spotifyId = _host.Icon(_host.PluginPack(PluginId, "logos")!, "spotify").Id;
	}

	[TearDown]
	public void TearDown() => _host.Dispose();

	[Test]
	public async Task A_provider_action_naming_its_own_icon_draws_that_icon_pack_icon()
	{
		var snapshot = await ProviderAction(PluginId, "logos/spotify").GetActionIconAsync(
			new Dictionary<string, object?>(),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(snapshot!.Reference!.Type, Is.EqualTo(WidgetIconReference.IconPackType));
			Assert.That(snapshot.Reference.Reference, Is.EqualTo(_spotifyId.ToString()));
		});
	}

	[Test]
	public async Task A_provider_action_of_another_plugin_never_reaches_this_plugins_pack()
	{
		var snapshot = await ProviderAction(OtherPluginId, "logos/spotify").GetActionIconAsync(
			new Dictionary<string, object?>(),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(snapshot!.Reference, Is.Null);
			Assert.That(snapshot.NoIcon, Is.True);
		});
	}

	[Test]
	public void A_ui_tree_naming_a_plugin_icon_carries_the_icon_pack_reference_instead()
	{
		var tree = Raw("""{"type":"image","value":{"type":"plugin-icon","reference":"logos/Spotify"},"label":"x"}""");

		var rewritten = PluginIconTreeRewriter.Rewrite(PluginId, tree, _host.Resolver).ToElement();

		Assert.Multiple(() =>
		{
			Assert.That(rewritten.GetProperty("value").GetProperty("type").GetString(), Is.EqualTo("icon-pack"));
			Assert.That(rewritten.GetProperty("value").GetProperty("reference").GetString(), Is.EqualTo(_spotifyId.ToString()));
			Assert.That(rewritten.GetProperty("label").GetString(), Is.EqualTo("x"));
		});
	}

	[Test]
	public void An_unresolvable_plugin_icon_in_a_ui_tree_becomes_unset()
	{
		var tree = Raw("""{"items":[{"type":"plugin-icon","reference":"logos/missing"}]}""");

		var rewritten = PluginIconTreeRewriter.Rewrite(OtherPluginId, tree, _host.Resolver).ToElement();

		Assert.That(rewritten.GetProperty("items")[0].ValueKind, Is.EqualTo(JsonValueKind.Null));
	}

	[Test]
	public void A_ui_tree_without_a_plugin_icon_is_relayed_byte_for_byte()
	{
		const string json = """{ "type" : "image",  "note":"plugin icons", "n": 1.50 }""";
		var tree = Raw(json);

		var rewritten = PluginIconTreeRewriter.Rewrite(PluginId, tree, _host.Resolver);

		Assert.That(Encoding.UTF8.GetString(rewritten.Utf8.Span), Is.EqualTo(json));
	}

	[Test]
	public void An_object_with_more_than_the_two_reference_properties_is_left_alone()
	{
		const string json = """{"type":"plugin-icon","reference":"logos/spotify","extra":true}""";

		var rewritten = PluginIconTreeRewriter.Rewrite(PluginId, Raw(json), _host.Resolver).ToElement();

		Assert.That(rewritten.GetProperty("type").GetString(), Is.EqualTo("plugin-icon"));
	}

	[Test]
	public void A_ui_tree_with_a_duplicate_key_is_relayed_unchanged_for_the_validator_to_judge()
	{
		const string json = """{"a":1,"a":2,"value":{"type":"plugin-icon","reference":"logos/spotify"}}""";

		var rewritten = PluginIconTreeRewriter.Rewrite(PluginId, Raw(json), _host.Resolver);

		Assert.That(Encoding.UTF8.GetString(rewritten.Utf8.Span), Is.EqualTo(json));
	}

	[Test]
	public void A_rewritten_ui_tree_keeps_text_unescaped()
	{
		var tree = Raw("""{"label":"Grüße <&>","value":{"type":"plugin-icon","reference":"logos/spotify"}}""");

		var rewritten = PluginIconTreeRewriter.Rewrite(PluginId, tree, _host.Resolver);

		Assert.That(Encoding.UTF8.GetString(rewritten.Utf8.Span), Does.Contain("Grüße <&>"));
	}

	private RemoteIconProviderAction ProviderAction(string pluginId, string reference)
		=> new(pluginId,
			"now-playing",
			new AnsweringInvoker(new ActionIconResult
			{
				HasValue = true,
				Version = "1",
				Reference = new ActionIconReferenceDto { Type = PluginIconReferences.Type, Reference = reference }
			}),
			new InMemoryPluginAssetCache(),
			_host.Resolver);

	private static UiRawJson Raw(string json) => UiRawJson.FromUtf8(Encoding.UTF8.GetBytes(json));

	private sealed class AnsweringInvoker(object answer) : IPluginCapabilityInvoker
	{
		public Task<JsonElement?> InvokeAsync(string pluginId,
			CapabilityInvokeRequest request,
			CancellationToken cancellationToken)
			=> Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(answer, PluginProtocolJson.Options));

		public bool TryComplete(string pluginId, ProtocolEnvelope result) => false;

		public void AbortAll(string pluginId, ProtocolError reason)
		{
		}

		public bool IsLiveActionExecute(string pluginId, string correlationId) => false;
	}
}
