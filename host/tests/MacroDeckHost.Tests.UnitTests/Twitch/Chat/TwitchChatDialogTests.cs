using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Tests.UnitTests.Streaming;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets.StreamChat;
using MacroDeckHost.Tests.UnitTests.Delegation;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Twitch.Chat;

[TestFixture]
internal sealed class TwitchChatDialogTests
{
	private const string Streamer = "111";
	private const string Client = "client-1";

	private StreamChatHub _hub = null!;
	private FakeFolderCache _folders = null!;
	private FakeHostLockState _hostLock = null!;
	private FakeIntegrationRegistry _integrations = null!;
	private RecordingModerator _moderator = null!;
	private RecordingUiInteractions _interactions = null!;
	private StreamChatWidgetUiProvider _provider = null!;
	private WidgetEntity _widget = null!;

	[SetUp]
	public void SetUp()
	{
		_hub = new StreamChatHub(new FakeTimeProvider(), Logger.None);
		_folders = new FakeFolderCache();
		_hostLock = new FakeHostLockState();
		_integrations = new FakeIntegrationRegistry();
		_moderator = new RecordingModerator();
		_integrations.Add(_moderator);
		_interactions = new RecordingUiInteractions();

		_widget = new WidgetEntity
		{
			Id = Guid.NewGuid(), Type = StreamPlatforms.Twitch.ChatWidgetTypeId, Data = """{"account":""}""",
		};
		_folders.AddWidget(_widget);

		_provider = new StreamChatWidgetUiProvider(StreamPlatforms.Twitch, _hub,
			new FakeTwitchChatImages(),
			TestLocalization.SampleText,
			_integrations,
			new UiResourceStore(),
			_interactions,
			_folders,
			_hostLock,
			Logger.None);

		_hub.SetAccounts([new ChatAccount(Streamer, "Streamer")]);
		_hub.Post(new ChatConnectionChanged(Streamer, true));
		_hub.Post(new ChatMessageReceived(Streamer, TwitchChatHubTests.Message("m1")));
		_hub.Tick();
	}

	[TearDown]
	public void TearDown() => _hub.Dispose();

	[Test]
	public async Task Pressing_a_placed_widget_opens_the_chat_dialog_on_the_pressing_client()
	{
		await using var widget = await OpenAsync(WidgetSurface(UiSurfaceKinds.Widget));

		Press(widget, widget.BuildTree().Root, Client);

		var (client, modal) = await _interactions.Opened.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(client, Is.EqualTo(Client));
			Assert.That(modal.ViewId, Is.EqualTo(StreamPlatforms.Twitch.DialogViewId));
			Assert.That(modal.Data![StreamChatWidgetType.AccountKey].GetString(), Is.EqualTo(Streamer));
			Assert.That(modal.Data!["widgetId"].GetString(), Is.EqualTo(_widget.Id.ToString()));
		});
	}

	[Test]
	public async Task A_press_without_a_client_to_show_it_on_opens_nothing()
	{
		await using var widget = await OpenAsync(WidgetSurface(UiSurfaceKinds.Widget));

		Press(widget, widget.BuildTree().Root, null);
		await Task.Delay(50);

		Assert.That(_interactions.Opened.Task.IsCompleted, Is.False);
	}

	[Test]
	public async Task The_editor_preview_and_the_picker_sample_are_not_pressable()
	{
		await using var preview = await OpenAsync(WidgetSurface(UiSurfaceKinds.Preview));
		await using var sample = await OpenAsync(WidgetSurface(UiSurfaceKinds.Preview, sample: true));

		Assert.Multiple(() =>
		{
			Assert.That(Pressable(preview.BuildTree().Root), Is.Empty);
			Assert.That(Pressable(sample.BuildTree().Root), Is.Empty);
		});
	}

	[Test]
	public async Task The_dialog_follows_the_end_of_the_chat_and_falls_back_to_newest_first()
	{
		_hub.Post(new ChatMessageReceived(Streamer, TwitchChatHubTests.Message("m2") with
		{
			Fragments = [new ChatFragment(ChatFragmentKind.Text, "second")],
		}));
		_hub.Tick();

		await using var dialog = await OpenDialogAsync();
		var list = Flatten(dialog.BuildTree().Root, includeFallback: false).Single(node => node.Type == UiComponents.List);
		var fallbackTexts = Texts(list.Fallback!);

		Assert.Multiple(() =>
		{
			Assert.That(list.RequiredComponentVersion, Is.EqualTo(3));
			Assert.That(list.Properties[UiComponentProperties.Anchor].GetString(), Is.EqualTo(UiComponentListAnchors.End));
			Assert.That(Texts(list), Has.Count.EqualTo(2));
			Assert.That(Texts(list)[1], Does.Contain("second"));
			Assert.That(fallbackTexts[0], Does.Contain("second"));
		});
	}

	[Test]
	public async Task A_deleted_message_leaves_the_dialog()
	{
		await using var dialog = await OpenDialogAsync();

		_hub.Post(new ChatMessageDeleted(Streamer, "m1"));
		_hub.Tick();

		Assert.That(Texts(dialog.BuildTree().Root), Has.Some.Contains("Integrations.StreamChat.Widget.Empty"));
	}

	[Test]
	public async Task Timing_out_from_a_message_runs_against_the_dialogs_account_and_reports_it()
	{
		await using var dialog = await OpenDialogAsync();

		SelectMessage(dialog);
		PressButton(dialog, "Timeout10Minutes");
		await WaitForAsync(() => Texts(dialog.BuildTree().Root).Any(text => text.Contains("StreamChat.Dialog.TimedOut")));

		Assert.That(_moderator.Requests,
			Is.EqualTo(new[] { (Streamer, ChatModerationRequest.Timeout("42", 600)) }));
	}

	[Test]
	public async Task A_ban_runs_only_after_it_is_confirmed()
	{
		await using var dialog = await OpenDialogAsync();

		SelectMessage(dialog);
		PressButton(dialog, "Ban");

		var askedFirst = Texts(dialog.BuildTree().Root).Any(text => text.Contains("StreamChat.Dialog.ConfirmBan"));
		var ranEarly = _moderator.Requests.Count;

		PressButton(dialog, "Ban");
		await WaitForAsync(() => Texts(dialog.BuildTree().Root).Any(text => text.Contains("StreamChat.Dialog.Banned")));

		Assert.Multiple(() =>
		{
			Assert.That(askedFirst, Is.True);
			Assert.That(ranEarly, Is.Zero);
			Assert.That(_moderator.Requests.Single().Request, Is.EqualTo(ChatModerationRequest.Ban("42")));
		});
	}

	[Test]
	public async Task Another_message_cannot_be_picked_while_an_action_is_still_running()
	{
		_hub.Post(new ChatMessageReceived(Streamer, TwitchChatHubTests.Message("m2", chatterId: "43") with
		{
			Fragments = [new ChatFragment(ChatFragmentKind.Text, "second")],
		}));
		_hub.Tick();
		var release = new TaskCompletionSource<ChatModerationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		_moderator.Pending = release.Task;
		await using var dialog = await OpenDialogAsync();

		SelectMessage(dialog);
		PressButton(dialog, "DeleteMessage");
		await WaitForAsync(() => _moderator.Requests.Count == 1);
		SelectMessage(dialog, index: 1);
		release.SetResult(ChatModerationResult.Succeeded);
		await WaitForAsync(() => Texts(dialog.BuildTree().Root).Any(text => text.Contains("StreamChat.Dialog.Deleted")));

		var selected = Flatten(dialog.BuildTree().Root, includeFallback: false)
			.Single(node => node.Id.EndsWith("selected", StringComparison.Ordinal));

		Assert.Multiple(() =>
		{
			Assert.That(selected.Properties[UiComponentProperties.Text].GetRawText(), Does.Contain("hello"));
			Assert.That(_moderator.Requests.Single().Request, Is.EqualTo(ChatModerationRequest.Delete("m1")));
		});
	}

	[Test]
	public async Task Closing_during_an_action_does_not_let_its_result_land_on_another_message()
	{
		_hub.Post(new ChatMessageReceived(Streamer, TwitchChatHubTests.Message("m2", chatterId: "43")));
		_hub.Tick();
		var release = new TaskCompletionSource<ChatModerationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		_moderator.Pending = release.Task;
		await using var dialog = await OpenDialogAsync();

		SelectMessage(dialog);
		PressButton(dialog, "DeleteMessage");
		await WaitForAsync(() => _moderator.Requests.Count == 1);
		PressButton(dialog, "Close");
		SelectMessage(dialog, index: 1);
		var reselectedWhileBusy = Texts(dialog.BuildTree().Root).Any(text => text.Contains("StreamChat.Dialog.DeleteMessage"));

		release.SetResult(ChatModerationResult.Succeeded);
		await WaitForAsync(() =>
		{
			SelectMessage(dialog, index: 1);
			return Texts(dialog.BuildTree().Root).Any(text => text.Contains("StreamChat.Dialog.DeleteMessage"));
		});

		Assert.Multiple(() =>
		{
			Assert.That(reselectedWhileBusy, Is.False);
			Assert.That(Texts(dialog.BuildTree().Root), Has.None.Contains("StreamChat.Dialog.Deleted"));
			Assert.That(_moderator.Requests, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_request_that_never_reached_twitch_is_not_blamed_on_twitch()
	{
		_moderator.Result = ChatModerationResult.Failed;
		await using var dialog = await OpenDialogAsync();

		SelectMessage(dialog);
		PressButton(dialog, "DeleteMessage");

		await WaitForAsync(() => Texts(dialog.BuildTree().Root).Any(text => text.Contains("StreamChat.Dialog.Failed")));
	}

	[Test]
	public async Task A_failure_is_reported_in_plain_words()
	{
		_moderator.Result = ChatModerationResult.NotPermitted;
		await using var dialog = await OpenDialogAsync();

		SelectMessage(dialog);
		PressButton(dialog, "DeleteMessage");

		await WaitForAsync(() => Texts(dialog.BuildTree().Root).Any(text => text.Contains("ChatDialog.NotPermitted")));
	}

	[Test]
	public async Task Nothing_is_moderated_while_the_host_is_locked()
	{
		await using var dialog = await OpenDialogAsync();
		_hostLock.IsLocked = true;

		SelectMessage(dialog);
		PressButton(dialog, "DeleteMessage");
		await WaitForAsync(() => Texts(dialog.BuildTree().Root).Any(text => text.Contains("Errors.Common.HostLocked")));

		Assert.That(_moderator.Requests, Is.Empty);
	}

	[Test]
	public async Task With_moderation_turned_off_the_dialog_only_shows_the_chat()
	{
		_widget.Data = """{"account":"","allowModeration":false}""";

		await using var dialog = await OpenDialogAsync();

		Assert.Multiple(() =>
		{
			Assert.That(Texts(dialog.BuildTree().Root), Has.Some.Contains("Viewer"));
			Assert.That(Pressable(dialog.BuildTree().Root), Is.Empty);
		});
	}

	[Test]
	public async Task Turning_moderation_off_while_the_dialog_is_open_refuses_the_next_action()
	{
		await using var dialog = await OpenDialogAsync();
		SelectMessage(dialog);

		_widget.Data = """{"account":"","allowModeration":false}""";
		PressButton(dialog, "DeleteMessage");
		await WaitForAsync(() =>
			Texts(dialog.BuildTree().Root).Any(text => text.Contains("StreamChat.Dialog.ModerationUnavailable")));

		Assert.That(_moderator.Requests, Is.Empty);
	}

	[Test]
	public async Task Moderation_stops_when_the_widget_switches_to_another_account()
	{
		await using var dialog = await OpenDialogAsync();
		SelectMessage(dialog);

		_widget.Data = """{"account":"222"}""";
		PressButton(dialog, "DeleteMessage");
		await WaitForAsync(() =>
			Texts(dialog.BuildTree().Root).Any(text => text.Contains("ChatDialog.AccountChanged")));

		Assert.That(_moderator.Requests, Is.Empty);
	}

	[Test]
	public async Task A_deleted_widget_gets_no_moderation()
	{
		_folders.RemoveWidget(Guid.Empty, _widget.Id);

		await using var dialog = await OpenDialogAsync();

		Assert.That(Pressable(dialog.BuildTree().Root), Is.Empty);
	}

	[Test]
	public async Task The_channels_own_messages_and_shared_chat_messages_offer_no_actions()
	{
		_hub.Post(new ChatMessageReceived(Streamer, TwitchChatHubTests.Message("own", chatterId: Streamer)));
		_hub.Post(new ChatMessageReceived(Streamer, TwitchChatHubTests.Message("shared") with
		{
			SourceChannelId = "777", SourceChannelName = "PartnerChannel",
		}));
		_hub.Tick();
		await using var dialog = await OpenDialogAsync();

		SelectMessage(dialog, index: 1);
		var own = Texts(dialog.BuildTree().Root);
		SelectMessage(dialog, index: 2);
		var shared = Texts(dialog.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(own, Has.Some.Contains("StreamChat.Dialog.OwnMessage"));
			Assert.That(own, Has.None.Contains("StreamChat.Dialog.DeleteMessage"));
			Assert.That(shared, Has.Some.Contains("ChatDialog.SharedChat").And.Contains("PartnerChannel"));
			Assert.That(shared, Has.None.Contains("StreamChat.Dialog.DeleteMessage"));
		});
	}

	[Test]
	public async Task A_disabled_twitch_integration_cannot_moderate()
	{
		_integrations.SetEnabled(StreamPlatforms.Twitch.OwnerId, false);
		await using var dialog = await OpenDialogAsync();

		SelectMessage(dialog);
		PressButton(dialog, "DeleteMessage");
		await WaitForAsync(() =>
			Texts(dialog.BuildTree().Root).Any(text => text.Contains("ChatDialog.AccountUnavailable")));

		Assert.That(_moderator.Requests, Is.Empty);
	}

	[Test]
	public async Task Moderation_reaches_only_the_integration_that_owns_the_platform()
	{
		var otherPlatform = new RecordingModerator(StreamPlatformTestSupport.TestPlatform.OwnerId);
		var integrations = new FakeIntegrationRegistry();
		integrations.Add(otherPlatform);
		integrations.Add(_moderator);
		var provider = new StreamChatWidgetUiProvider(StreamPlatforms.Twitch,
			_hub,
			new FakeTwitchChatImages(),
			TestLocalization.SampleText,
			integrations,
			new UiResourceStore(),
			_interactions,
			_folders,
			_hostLock,
			Logger.None);
		await using var dialog = (await provider.CreateSessionAsync(
			new UiSessionRequest { Surface = DialogSurface(), UiModelVersion = 1 },
			CancellationToken.None))!;

		SelectMessage(dialog);
		PressButton(dialog, "DeleteMessage");
		await WaitForAsync(() => Texts(dialog.BuildTree().Root).Any(text => text.Contains("StreamChat.Dialog.Deleted")));

		Assert.Multiple(() =>
		{
			Assert.That(_moderator.Requests, Is.EqualTo(new[] { (Streamer, ChatModerationRequest.Delete("m1")) }));
			Assert.That(otherPlatform.Requests, Is.Empty);
		});
	}

	[Test]
	public async Task Another_platforms_moderator_does_not_stand_in_for_a_missing_twitch_integration()
	{
		var otherPlatform = new RecordingModerator(StreamPlatformTestSupport.TestPlatform.OwnerId);
		var integrations = new FakeIntegrationRegistry();
		integrations.Add(otherPlatform);
		var provider = new StreamChatWidgetUiProvider(StreamPlatforms.Twitch,
			_hub,
			new FakeTwitchChatImages(),
			TestLocalization.SampleText,
			integrations,
			new UiResourceStore(),
			_interactions,
			_folders,
			_hostLock,
			Logger.None);
		await using var dialog = (await provider.CreateSessionAsync(
			new UiSessionRequest { Surface = DialogSurface(), UiModelVersion = 1 },
			CancellationToken.None))!;

		SelectMessage(dialog);
		PressButton(dialog, "DeleteMessage");
		await WaitForAsync(() =>
			Texts(dialog.BuildTree().Root).Any(text => text.Contains("ChatDialog.AccountUnavailable")));

		Assert.That(otherPlatform.Requests, Is.Empty);
	}

	[Test]
	public async Task An_action_the_platform_does_not_support_reports_a_generic_failure()
	{
		_moderator.Result = ChatModerationResult.Unsupported;
		await using var dialog = await OpenDialogAsync();

		SelectMessage(dialog);
		PressButton(dialog, "DeleteMessage");
		await WaitForAsync(() => Texts(dialog.BuildTree().Root).Any(text => text.Contains("StreamChat.Dialog.Failed")));

		Assert.That(Texts(dialog.BuildTree().Root), Has.None.Contains("Integrations.Twitch.ChatDialog"));
	}

	[Test]
	public async Task A_full_history_of_heavy_messages_stays_within_the_tree_limit()
	{
		var images = new FakeTwitchChatImages(new UiResourceStore());
		var provider = new StreamChatWidgetUiProvider(StreamPlatforms.Twitch, _hub, images, TestLocalization.SampleText,
			_integrations,
			new UiResourceStore(), _interactions, _folders, _hostLock, Logger.None);

		for (var index = 0; index < 120; index++)
		{
			var fragments = new List<ChatFragment>();

			for (var emote = 0; emote < 20; emote++)
			{
				var emoteId = $"emotesv2_{index}_{emote}".PadRight(64, 'x');
				images.Resolve(TwitchChatImage.Emote(emoteId));
				fragments.Add(new ChatFragment(ChatFragmentKind.Text, "😀漢字テスト "));
				fragments.Add(new ChatFragment(ChatFragmentKind.Emote, "PogChamp", emoteId));
			}

			_hub.Post(new ChatMessageReceived(Streamer,
				TwitchChatHubTests.Message($"heavy-{index}") with { Fragments = fragments }));
		}

		_hub.Tick();

		await using var dialog = (await provider.CreateSessionAsync(
			new UiSessionRequest { Surface = DialogSurface(), UiModelVersion = 1 }, CancellationToken.None))!;
		var root = dialog.BuildTree().Root;

		Assert.Multiple(() =>
		{
			Assert.That(UiCanonicalJson.SerializeToUtf8Bytes(root).Length, Is.LessThanOrEqualTo(ProtocolLimits.MaxUiTreeBytes));
			Assert.That(Flatten(root).Count(), Is.LessThanOrEqualTo(ProtocolLimits.MaxUiNodesPerTree));
		});
	}

	[Test]
	public async Task Chat_updates_and_presses_arriving_together_never_block_each_other()
	{
		// Disposed only once both loops ended: disposing takes the same locks a deadlock would hold.
		var widget = await OpenAsync(WidgetSurface(UiSurfaceKinds.Widget));
		var dialog = await OpenDialogAsync();
		var widgetRoot = widget.BuildTree().Root;
		using var stop = new CancellationTokenSource();

		var feed = Task.Run(() =>
		{
			for (var index = 0; !stop.IsCancellationRequested; index++)
			{
				_hub.Post(new ChatMessageReceived(Streamer, TwitchChatHubTests.Message($"load-{index}")));
				_hub.Tick();
			}
		});

		var presses = Task.Run(() =>
		{
			for (var index = 0; index < 300; index++)
			{
				Press(widget, widgetRoot, Client);
				SelectMessage(dialog);

				var close = Flatten(dialog.BuildTree().Root, includeFallback: false)
					.LastOrDefault(node => node.Type == UiComponents.Button &&
						Texts(node).Any(text => text.Contains(".Close\"", StringComparison.Ordinal)));

				if (close is not null)
				{
					dialog.Dispatch(new UiEvent { NodeId = close.Id, Name = UiComponentEvents.Press });
				}
			}
		});

		var finished = await Task.WhenAny(presses, Task.Delay(TimeSpan.FromSeconds(20)));
		await stop.CancelAsync();

		Assert.That(finished, Is.SameAs(presses), "a press never returned while the chat was updating");
		Assert.That(await Task.WhenAny(feed, Task.Delay(TimeSpan.FromSeconds(20))), Is.SameAs(feed),
			"the chat stopped updating while presses arrived");
		await presses;
		await dialog.DisposeAsync();
		await widget.DisposeAsync();
	}

	private async Task<IUiSession> OpenAsync(UiSurface surface)
		=> (await _provider.CreateSessionAsync(new UiSessionRequest { Surface = surface, UiModelVersion = 1 },
			CancellationToken.None))!;

	private Task<IUiSession> OpenDialogAsync() => OpenAsync(DialogSurface());

	private UiSurface DialogSurface()
		=> new()
		{
			Kind = UiSurfaceKinds.Dialog,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiDialogSurfaceAttributes.ModalId] = JsonSerializer.SerializeToElement("modal-1"),
				[UiDialogSurfaceAttributes.ViewId] = JsonSerializer.SerializeToElement(StreamPlatforms.Twitch.DialogViewId),
				[UiDialogSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(new Dictionary<string, string>
				{
					["account"] = Streamer, ["widgetId"] = _widget.Id.ToString(),
				}),
			},
		};

	private UiSurface WidgetSurface(string kind, bool sample = false)
	{
		var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[UiWidgetSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement(StreamPlatforms.Twitch.ChatWidgetTypeId),
			[UiWidgetSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(new { account = "" }),
		};

		if (kind == UiSurfaceKinds.Widget)
		{
			attributes[UiWidgetSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(_widget.Id.ToString());
		}

		if (sample)
		{
			attributes[UiWidgetSurfaceAttributes.Sample] = JsonSerializer.SerializeToElement(true);
		}

		return new UiSurface { Kind = kind, SessionMode = UiSessionModes.Shared, Attributes = attributes };
	}

	private static void Press(IUiSession session, UiNode node, string? client)
		=> ((IOriginAwareUiSession)session).Dispatch(new UiEvent { NodeId = node.Id, Name = UiComponentEvents.Press },
			client);

	private static void SelectMessage(IUiSession session, int index = 0)
	{
		var list = Flatten(session.BuildTree().Root, includeFallback: false).Single(node => node.Type == UiComponents.List);
		session.Dispatch(new UiEvent { NodeId = Pressable(list)[index].Id, Name = UiComponentEvents.Press });
	}

	private static void PressButton(IUiSession session, string key)
	{
		var button = Flatten(session.BuildTree().Root, includeFallback: false)
			.Where(node => node.Type == UiComponents.Button)
			.Last(node => Texts(node).Any(text => text.Contains("." + key + "\"", StringComparison.Ordinal)));

		session.Dispatch(new UiEvent { NodeId = button.Id, Name = UiComponentEvents.Press });
	}

	private static async Task WaitForAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);

		while (!condition())
		{
			Assert.That(DateTime.UtcNow, Is.LessThan(deadline), "the dialog never reached the expected state");
			await Task.Delay(10);
		}
	}

	private static List<UiNode> Pressable(UiNode root)
		=> [.. Flatten(root).Where(node => node.Properties.TryGetValue(UiComponentProperties.Events, out var events) &&
			events.EnumerateArray().Any(item => item.GetRawText().Contains(UiComponentEvents.Press, StringComparison.Ordinal)))];

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
		=> [.. Flatten(root, includeFallback: false)
			.Where(node => node.Properties.ContainsKey(UiComponentProperties.Text))
			.Select(node => node.Properties[UiComponentProperties.Text].GetRawText())];

	private sealed class RecordingModerator(string? id = null) : IIntegration, IStreamChatModerator
	{
		public List<(string AccountId, ChatModerationRequest Request)> Requests { get; } = [];

		public ChatModerationResult Result { get; set; } = ChatModerationResult.Succeeded;

		public Task<ChatModerationResult>? Pending { get; set; }

		public string Id => id ?? StreamPlatforms.Twitch.OwnerId;

		public LocalizedText Name => "Twitch";

		public string Version => "1.0.0";

		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public bool IsInitialized => true;

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public Task<ChatModerationResult> ModerateAsync(
			string accountId,
			ChatModerationRequest request,
			CancellationToken cancellationToken)
		{
			lock (Requests)
			{
				Requests.Add((accountId, request));
			}

			return Pending ?? Task.FromResult(Result);
		}
	}
}
