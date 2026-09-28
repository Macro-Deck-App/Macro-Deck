using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Twitch.Chat;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets.TwitchChat;
using MacroDeckHost.Tests.UnitTests.Delegation;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Twitch.Chat;

[TestFixture]
internal sealed class TwitchChatWidgetViewTests
{
	private const string Streamer = "111";

	private UiResourceStore _store = null!;
	private FakeTwitchChatImages _images = null!;
	private TwitchChatHub _hub = null!;
	private TwitchChatWidgetUiProvider _provider = null!;

	[SetUp]
	public void SetUp()
	{
		_store = new UiResourceStore();
		_images = new FakeTwitchChatImages(_store);
		_hub = new TwitchChatHub(new FakeTimeProvider(), Logger.None);
		var integrations = new FakeIntegrationRegistry();
		integrations.Add(new IconIntegration());
		_provider = new TwitchChatWidgetUiProvider(_hub, _images, TestLocalization.SampleText, integrations, _store);
	}

	[TearDown]
	public void TearDown() => _hub.Dispose();

	[Test]
	public async Task Without_a_connection_the_widget_says_it_is_not_connected()
	{
		_hub.SetAccounts([new TwitchChatAccount(Streamer, "Streamer")]);
		_hub.Tick();

		await using var session = await OpenAsync();

		Assert.That(Texts(session.BuildTree().Root), Has.Some.Contains("Integrations.Twitch.ChatWidget.Offline"));
	}

	[Test]
	public async Task A_connected_chat_without_messages_says_it_is_waiting()
	{
		Connect();

		await using var session = await OpenAsync();

		Assert.That(Texts(session.BuildTree().Root), Has.Some.Contains("Integrations.Twitch.ChatWidget.Empty"));
	}

	[Test]
	public async Task A_message_shows_the_name_in_its_colour_and_the_emote_as_its_image()
	{
		Connect();
		var emote = _images.Resolve(TwitchChatImage.Emote("25"));
		await using var session = await OpenAsync();

		_hub.Post(new TwitchChatMessageReceived(Streamer, TwitchChatHubTests.Message("m1", emoteId: "25")));
		_hub.Tick();

		var line = MessageNodes(session.BuildTree().Root).Single();
		var spans = line.Properties[UiComponentProperties.Spans].EnumerateArray().ToList();
		var image = spans.Single(span => span.TryGetProperty("image", out _));

		Assert.Multiple(() =>
		{
			Assert.That(line.Properties[UiComponentProperties.Text].GetRawText(), Does.Contain("Viewer: hello Kappa"));
			Assert.That(spans[0].GetProperty("text").GetString(), Is.EqualTo("Viewer"));
			Assert.That(spans[0].GetProperty("color").GetString(), Is.EqualTo("#1e90ff"));
			Assert.That(spans[1].GetProperty("text").GetString(), Is.EqualTo(": "));
			Assert.That(image.GetProperty("image").GetProperty("resourceId").GetString(), Is.EqualTo(emote.ResourceId));
			Assert.That(image.GetProperty("alt").GetString(), Is.EqualTo("Kappa"));
			Assert.That(_store.TryGet(emote.ResourceId, out _), Is.True);
		});
	}

	[Test]
	public async Task An_emote_whose_image_is_not_there_yet_reads_as_its_code()
	{
		Connect();
		await using var session = await OpenAsync();

		_hub.Post(new TwitchChatMessageReceived(Streamer, TwitchChatHubTests.Message("m1", emoteId: "25")));
		_hub.Tick();

		var spans = MessageNodes(session.BuildTree().Root).Single().Properties[UiComponentProperties.Spans];
		Assert.Multiple(() =>
		{
			Assert.That(spans.EnumerateArray().Any(span => span.TryGetProperty("image", out _)), Is.False);
			Assert.That(spans.EnumerateArray().Last().GetProperty("text").GetString(), Is.EqualTo("hello Kappa"));
		});
	}

	[Test]
	public async Task The_log_clips_old_lines_and_gives_older_readers_a_short_excerpt_under_other_ids()
	{
		Connect();
		await using var session = await OpenAsync();

		for (var index = 0; index < 5; index++)
		{
			_hub.Post(new TwitchChatMessageReceived(Streamer, TwitchChatHubTests.Message("m" + index)));
		}

		_hub.Tick();

		var log = Flatten(session.BuildTree().Root).Single(node => node.Fallback is not null);
		var primaryIds = MessageNodes(log).Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
		var fallbackLines = log.Fallback!.Children;

		Assert.Multiple(() =>
		{
			Assert.That(log.RequiredComponentVersion, Is.EqualTo(2));
			Assert.That(log.Properties[UiComponentProperties.Overflow].GetString(),
				Is.EqualTo(UiComponentOverflows.ClipStart));
			Assert.That(primaryIds, Has.Count.EqualTo(5));
			Assert.That(fallbackLines, Has.Count.EqualTo(3));
			Assert.That(fallbackLines.Select(node => node.Id), Has.None.Matches<string>(primaryIds.Contains));
			Assert.That(fallbackLines.Select(node => node.Properties[UiComponentProperties.MaxLines].GetInt32()),
				Is.All.EqualTo(2));
		});
	}

	[Test]
	public async Task A_deleted_message_disappears_from_the_tile()
	{
		Connect();
		await using var session = await OpenAsync();
		_hub.Post(new TwitchChatMessageReceived(Streamer, TwitchChatHubTests.Message("gone")));
		_hub.Post(new TwitchChatMessageReceived(Streamer, TwitchChatHubTests.Message("stays")));
		_hub.Tick();

		_hub.Post(new TwitchChatMessageDeleted(Streamer, "gone"));
		_hub.Tick();

		var ids = MessageNodes(session.BuildTree().Root).Select(node => node.Id).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(ids, Has.Count.EqualTo(1));
			Assert.That(ids[0], Does.EndWith(TwitchChatStyle.MessageKey("stays")));
		});
	}

	[Test]
	public async Task A_busy_chat_stays_inside_the_tree_and_patch_limits_even_when_every_line_is_replaced()
	{
		Connect();
		await using var session = await OpenAsync();
		session.DrainPatches();

		var patchSizes = new List<int>();

		for (var burst = 0; burst < 2; burst++)
		{
			for (var index = 0; index < 100; index++)
			{
				_hub.Post(new TwitchChatMessageReceived(Streamer, HeavyMessage($"b{burst}-{index}")));
			}

			_hub.Tick();
			patchSizes.AddRange(session.DrainPatches()
				.Select(patch => UiCanonicalJson.SerializeToUtf8Bytes(patch).Length));
		}

		var tree = session.BuildTree();
		var lines = MessageNodes(tree.Root).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(patchSizes, Is.Not.Empty);
			Assert.That(patchSizes, Is.All.LessThan(ProtocolLimits.MaxUiPatchBytes));
			Assert.That(UiCanonicalJson.SerializeToUtf8Bytes(tree).Length, Is.LessThan(ProtocolLimits.MaxUiTreeBytes));
			Assert.That(lines, Is.Not.Empty.And.Count.LessThanOrEqualTo(TwitchChatLines.MaxMessages));
			Assert.That(lines[^1].Id, Does.EndWith(TwitchChatStyle.MessageKey("b1-99")), "the newest line is shown");
		});
	}

	[Test]
	public async Task A_french_host_separates_name_and_message_the_french_way()
	{
		var preferences = (FakeLocalizationPreferences)TestLocalization.Preferences;
		var previous = preferences.Culture;
		preferences.Culture = "fr";

		try
		{
			Connect();
			await using var session = await OpenAsync();
			_hub.Post(new TwitchChatMessageReceived(Streamer, TwitchChatHubTests.Message("m1")));
			_hub.Tick();

			var spans = MessageNodes(session.BuildTree().Root).Single().Properties[UiComponentProperties.Spans];
			Assert.That(spans[1].GetProperty("text").GetString(), Is.EqualTo(" : "));
		}
		finally
		{
			preferences.Culture = previous;
		}
	}

	[Test]
	public async Task The_picker_sample_shows_example_lines_without_reading_chat()
	{
		var surface = Surface(UiSurfaceKinds.Preview, sample: true);

		var request = new UiSessionRequest { Surface = surface, UiModelVersion = 1 };
		await using var session = (await _provider.CreateSessionAsync(request, CancellationToken.None))!;

		Assert.That(MessageNodes(session.BuildTree().Root).Count(), Is.EqualTo(3));
	}

	private TwitchChatMessage HeavyMessage(string id)
	{
		var fragments = new List<TwitchChatFragment>();

		for (var index = 0; index < 20; index++)
		{
			var emoteId = ("emotesv2_" + id.Replace('-', '_') + "_" + index).PadRight(64, 'x');
			_images.Resolve(TwitchChatImage.Emote(emoteId));
			fragments.Add(new TwitchChatFragment(TwitchChatFragmentKind.Text, "😀漢字テスト "));
			fragments.Add(new TwitchChatFragment(TwitchChatFragmentKind.Emote, "PogChamp", emoteId));
		}

		return new TwitchChatMessage(id,
			"42",
			"viewer",
			"Viewer",
			"#ff4500",
			[new TwitchChatBadge("subscriber", "12", null)],
			fragments);
	}

	[Test]
	public async Task The_tile_is_titled_with_the_twitch_mark_and_the_accounts_name()
	{
		Connect();

		await using var session = await OpenAsync();

		var root = session.BuildTree().Root;
		var icon = Flatten(root).Single(node => node.Type == UiComponents.Image);
		var resourceId = icon.Properties[UiComponentProperties.Source].GetProperty("resourceId").GetString()!;

		Assert.Multiple(() =>
		{
			Assert.That(Texts(root), Has.Some.Contains("Integrations.Twitch.ChatWidget.Title").And.Contains("Streamer"));
			Assert.That(_store.TryGet(resourceId, out _), Is.True);
		});
	}

	[Test]
	public async Task Badges_are_spaced_apart_and_from_the_name()
	{
		Connect();
		_images.Resolve(TwitchChatImage.Badge("broadcaster", "1", "https://static-cdn.jtvnw.net/badges/v1/b/2"));
		_images.Resolve(TwitchChatImage.Badge("subscriber", "12", "https://static-cdn.jtvnw.net/badges/v1/s/2"));
		await using var session = await OpenAsync();

		_hub.Post(new TwitchChatMessageReceived(Streamer, TwitchChatHubTests.Message("m1") with
		{
			Badges =
			[
				new TwitchChatBadge("broadcaster", "1", "https://static-cdn.jtvnw.net/badges/v1/b/2"),
				new TwitchChatBadge("subscriber", "12", "https://static-cdn.jtvnw.net/badges/v1/s/2"),
			],
		}));
		_hub.Tick();

		var spans = MessageNodes(session.BuildTree().Root).Single()
			.Properties[UiComponentProperties.Spans].EnumerateArray().ToList();

		Assert.Multiple(() =>
		{
			Assert.That(spans[0].TryGetProperty("image", out _), Is.True);
			Assert.That(spans[1].GetProperty("text").GetString(), Is.Not.Empty.And.Matches(@"^\s+$"));
			Assert.That(spans[2].TryGetProperty("image", out _), Is.True);
			Assert.That(spans[3].GetProperty("text").GetString(), Is.Not.Empty.And.Matches(@"^\s+$"));
			Assert.That(spans[4].GetProperty("text").GetString(), Is.EqualTo("Viewer"));
		});
	}

	private sealed class IconIntegration : IIntegration, IIntegrationIconProvider
	{
		public string Id => TwitchChatWidgetType.OwnerId;

		public LocalizedText Name => "Twitch";

		public string Version => "1.0.0";

		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public bool IsInitialized => true;

		public string IconMimeType => "image/svg+xml";

		public byte[] GetIcon() => "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray();

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;
	}

	private void Connect()
	{
		_hub.SetAccounts([new TwitchChatAccount(Streamer, "Streamer")]);
		_hub.Post(new TwitchChatConnectionChanged(Streamer, true));
		_hub.Tick();
	}

	private async Task<IUiSession> OpenAsync()
		=> (await _provider.CreateSessionAsync(
			new UiSessionRequest { Surface = Surface(UiSurfaceKinds.Widget), UiModelVersion = 1 },
			CancellationToken.None))!;

	private static UiSurface Surface(string kind, bool sample = false)
	{
		var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[UiWidgetSurfaceAttributes.WidgetType]
				= JsonSerializer.SerializeToElement(TwitchChatWidgetType.QualifiedId),
			[UiWidgetSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(new { account = "" }),
		};

		if (sample)
		{
			attributes[UiWidgetSurfaceAttributes.Sample] = JsonSerializer.SerializeToElement(true);
		}

		return new UiSurface { Kind = kind, SessionMode = UiSessionModes.Shared, Attributes = attributes };
	}

	private static IEnumerable<UiNode> MessageNodes(UiNode root)
		=> Flatten(root, includeFallback: false)
			.Where(node => node.Type == UiComponents.Text && node.Properties.ContainsKey(UiComponentProperties.Spans));

	private static IEnumerable<UiNode> Flatten(UiNode node, bool includeFallback = true)
	{
		yield return node;

		foreach (var child in node.Children.SelectMany(child => Flatten(child, includeFallback)))
		{
			yield return child;
		}

		if (includeFallback && node.Fallback is { } fallback)
		{
			foreach (var child in Flatten(fallback, includeFallback))
			{
				yield return child;
			}
		}
	}

	private static List<string> Texts(UiNode root)
		=> [.. Flatten(root).Where(node => node.Properties.ContainsKey(UiComponentProperties.Text))
			.Select(node => node.Properties[UiComponentProperties.Text].GetRawText())];
}
