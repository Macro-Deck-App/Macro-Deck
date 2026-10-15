using System.Text.Json;
using MacroDeck.Sdk.Identity;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Integrations.Steam.Events;
using MacroDeckHost.Integrations.Steam.Native;
using MacroDeckHost.Integrations.Steam.Protocol;
using MacroDeckHost.Integrations.Steam.State;
using MacroDeckHost.Integrations.Steam.Widgets;
using MacroDeckHost.Tests.UnitTests.Delegation;

namespace MacroDeckHost.Tests.UnitTests.Steam;

[TestFixture]
internal sealed class SteamEventTests
{
	private const string SteamId = "76561198000000001";

	private SteamFolders _folders = null!;
	private FakeTimeProvider _time = null!;
	private FakeSteamPlatform _platform = null!;
	private readonly List<SteamEvent> _events = [];

	[SetUp]
	public void SetUp()
	{
		_folders = new SteamFolders();
		_time = new FakeTimeProvider();
		var steam = _folders.Make("Steam");
		_platform = new FakeSteamPlatform
			{ Os = SteamOs.Windows, HomeDirectory = _folders.Root, RegistrySteamPath = steam };
		_events.Clear();
		SteamFolders.WriteManifest(steam, 730, "Counter-Strike 2");
		SteamFolders.WriteManifest(steam, 220, "Half-Life 2");
	}

	[TearDown]
	public void TearDown()
	{
		_folders.Dispose();
	}

	[Test]
	public async Task What_is_already_going_on_when_tracking_starts_is_not_reported()
	{
		var web = new ScriptedWebApi { Self = Self(SteamPersona.Online, 440), Friends = [Summary("f1", 1, 570)] };
		_platform.RegistryRunningAppId = 730;
		var tracker = Tracker(Account("e1", web));

		tracker.PollLocal();
		await tracker.PollSelfAsync(CancellationToken.None);
		await tracker.PollFriendsAsync(CancellationToken.None);

		Assert.That(_events, Is.Empty);
	}

	[Test]
	public void A_game_starting_and_stopping_here_is_reported_once_with_how_long_it_ran()
	{
		var tracker = Tracker(new SteamAccountConfig("e1", "Main", "main"), new SteamAccountConfig("e2", "Alt", "alt"));
		tracker.PollLocal();

		_platform.RegistryRunningAppId = 730;
		tracker.PollLocal();
		_time.Advance(TimeSpan.FromMinutes(5));
		_platform.RegistryRunningAppId = 0;
		tracker.PollLocal();

		Assert.That(_events.Select(item => item.Id),
			Is.EqualTo(new[] { SteamEventIds.GameStarted, SteamEventIds.GameStopped }));
		Assert.Multiple(() =>
		{
			Assert.That(_events[0].Payload[SteamEventPayload.Configuration],
				Is.EqualTo(SteamEventPayload.ThisComputer));
			Assert.That(_events[0].Payload[SteamEventPayload.GameId], Is.EqualTo(730));
			Assert.That(_events[0].Payload[SteamEventPayload.GameName], Is.EqualTo("Counter-Strike 2"));
			Assert.That(_events[1].Payload[SteamEventPayload.DurationSeconds], Is.EqualTo(300L));
		});
	}

	[Test]
	public async Task A_game_an_account_plays_on_another_device_is_reported_for_that_account()
	{
		var web = new ScriptedWebApi { Self = Self(SteamPersona.Online) };
		var tracker = Tracker(Account("e1", web));
		tracker.PollLocal();
		await tracker.PollSelfAsync(CancellationToken.None);

		web.Self = Self(SteamPersona.Online, 220, "Half-Life 2");
		await tracker.PollSelfAsync(CancellationToken.None);
		web.Self = Self(SteamPersona.Online);
		await tracker.PollSelfAsync(CancellationToken.None);

		Assert.That(_events.Select(item => (item.Id, item.Payload[SteamEventPayload.Configuration])),
			Is.EqualTo(new[]
				{ (SteamEventIds.GameStarted, (object?)"e1"), (SteamEventIds.GameStopped, (object?)"e1") }));
	}

	[Test]
	public async Task A_game_that_started_while_steam_could_not_be_reached_is_not_reported_afterwards()
	{
		var web = new ScriptedWebApi { Self = Self(SteamPersona.Online) };
		var tracker = Tracker(Account("e1", web));
		tracker.PollLocal();
		await tracker.PollSelfAsync(CancellationToken.None);

		web.SelfFailure = SteamWebFailure.Unavailable;
		for (var attempt = 0; attempt < 3; attempt++)
		{
			await tracker.PollSelfAsync(CancellationToken.None);
		}

		web.SelfFailure = null;
		web.Self = Self(SteamPersona.Online, 220, "Half-Life 2");
		await tracker.PollSelfAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(tracker.Snapshot.Accounts[0].CurrentGame?.AppId,
				Is.EqualTo(220),
				"precondition: the game is seen");
			Assert.That(_events, Is.Empty);
		});
	}

	[Test]
	public async Task A_game_the_web_api_reports_before_this_computer_notices_it_is_not_reported_as_stopped()
	{
		var web = new ScriptedWebApi { Self = Self(SteamPersona.Online) };
		var tracker = Tracker(Account("e1", web));
		tracker.PollLocal();
		await tracker.PollSelfAsync(CancellationToken.None);

		web.Self = Self(SteamPersona.Online, 730);
		await tracker.PollSelfAsync(CancellationToken.None);
		_platform.RegistryRunningAppId = 730;
		tracker.PollLocal();

		Assert.That(_events.Where(item => item.Id == SteamEventIds.GameStopped), Is.Empty);
	}

	[Test]
	public async Task A_local_game_is_not_reported_again_for_the_account_that_plays_it()
	{
		var web = new ScriptedWebApi { Self = Self(SteamPersona.Online) };
		var tracker = Tracker(Account("e1", web));
		tracker.PollLocal();
		await tracker.PollSelfAsync(CancellationToken.None);

		_platform.RegistryRunningAppId = 730;
		tracker.PollLocal();
		web.Self = Self(SteamPersona.Online, 730);
		await tracker.PollSelfAsync(CancellationToken.None);

		Assert.That(_events.Select(item => item.Payload[SteamEventPayload.Configuration]),
			Is.EqualTo(new object?[] { SteamEventPayload.ThisComputer }));
	}

	[Test]
	public async Task Friends_coming_online_going_offline_and_starting_games_are_reported()
	{
		var web = new ScriptedWebApi
		{
			Self = Self(SteamPersona.Online),
			Friends =
			[
				Summary("a", SteamPersona.Offline), Summary("b", SteamPersona.Online),
				Summary("c", SteamPersona.Online, 570)
			]
		};
		var tracker = Tracker(Account("e1", web));
		await tracker.PollSelfAsync(CancellationToken.None);
		await tracker.PollFriendsAsync(CancellationToken.None);

		web.Friends =
		[
			Summary("a", SteamPersona.Online), Summary("b", SteamPersona.Offline),
			Summary("c", SteamPersona.Online, 730)
		];
		await tracker.PollFriendsAsync(CancellationToken.None);

		Assert.That(_events.Select(item => (item.Id, item.Payload[SteamEventPayload.FriendId])),
			Is.EquivalentTo(new[]
			{
				(SteamEventIds.FriendOnline, (object?)"a"),
				(SteamEventIds.FriendOffline, (object?)"b"),
				(SteamEventIds.FriendStartedGame, (object?)"c")
			}));
		Assert.That(_events.All(item => Equals(item.Payload[SteamEventPayload.Configuration], "e1")), Is.True);
	}

	[Test]
	public async Task A_friend_going_from_offline_straight_into_a_game_is_reported_as_online_and_playing()
	{
		var web = new ScriptedWebApi
			{ Self = Self(SteamPersona.Online), Friends = [Summary("a", SteamPersona.Offline)] };
		var tracker = Tracker(Account("e1", web));
		await tracker.PollSelfAsync(CancellationToken.None);
		await tracker.PollFriendsAsync(CancellationToken.None);

		web.Friends = [Summary("a", SteamPersona.Online, 730)];
		await tracker.PollFriendsAsync(CancellationToken.None);

		Assert.That(_events.Select(item => item.Id),
			Is.EqualTo(new[] { SteamEventIds.FriendOnline, SteamEventIds.FriendStartedGame }));
	}

	[Test]
	public async Task A_failed_friends_poll_and_its_recovery_report_nothing()
	{
		var web = new ScriptedWebApi
		{
			Self = Self(SteamPersona.Online),
			Friends = [Summary("a", SteamPersona.Online), Summary("b", SteamPersona.Online, 570)]
		};
		var tracker = Tracker(Account("e1", web));
		await tracker.PollSelfAsync(CancellationToken.None);
		await tracker.PollFriendsAsync(CancellationToken.None);

		web.FriendsFailure = SteamWebFailure.Unavailable;
		await tracker.PollFriendsAsync(CancellationToken.None);
		web.FriendsFailure = null;
		await tracker.PollFriendsAsync(CancellationToken.None);

		Assert.That(_events, Is.Empty);
	}

	[Test]
	public async Task A_change_of_the_own_online_status_is_reported_but_losing_the_web_api_is_not()
	{
		var web = new ScriptedWebApi { Self = Self(SteamPersona.Online) };
		var tracker = Tracker(Account("e1", web));
		await tracker.PollSelfAsync(CancellationToken.None);

		web.Self = Self(SteamPersona.Away);
		await tracker.PollSelfAsync(CancellationToken.None);
		web.SelfFailure = SteamWebFailure.InvalidKey;
		await tracker.PollSelfAsync(CancellationToken.None);

		Assert.That(_events, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(_events[0].Id, Is.EqualTo(SteamEventIds.PersonaChanged));
			Assert.That(_events[0].Payload[SteamEventPayload.State], Is.EqualTo("away"));
			Assert.That(_events[0].Payload[SteamEventPayload.PreviousState], Is.EqualTo("online"));
		});
	}

	[Test]
	public async Task A_trigger_set_to_one_account_ignores_the_occurrences_of_another()
	{
		var definition = SteamEventDefinitions.All.Single(item => item.Id == SteamEventIds.FriendOnline);
		var occurrence = SteamEventFactory.FriendChanges("e2",
				[Friend("a", SteamPersona.Offline)],
				[Friend("a", SteamPersona.Online)])
			.Single();

		var other = await MatchesAsync(definition, occurrence, "e1");
		var same = await MatchesAsync(definition, occurrence, "e2");

		Assert.Multiple(() =>
		{
			Assert.That(other, Is.False);
			Assert.That(same, Is.True);
		});
	}

	[Test]
	public void Every_event_can_be_narrowed_to_an_account_and_carries_it()
	{
		foreach (var definition in SteamEventDefinitions.All)
		{
			Assert.That(definition.ConfigurationParameters.Select(parameter => parameter.Name),
				Does.Contain(SteamEventPayload.Configuration),
				definition.Id);
			Assert.That(definition.PayloadParameters.Select(parameter => parameter.Name),
				Is.SupersetOf(definition.ConfigurationParameters.Select(parameter => parameter.Name)),
				definition.Id);
		}
	}

	private static async Task<bool> MatchesAsync(
		MacroDeck.Sdk.Events.EventDefinition definition,
		SteamEvent occurrence,
		string account)
	{
		var eventId = $"{SteamWidgetType.OwnerId}::{definition.Id}";
		var descriptor = new EventDefinitionDescriptor(QualifiedId.Parse(eventId),
			SteamWidgetType.OwnerId,
			"Steam",
			false,
			definition);
		var subscription = new EventSubscription(EventTriggerOwner.ForWidget(Guid.NewGuid()),
			"t1",
			eventId,
			new Dictionary<string, EventConfigurationValue>(StringComparer.Ordinal)
			{
				[SteamEventPayload.Configuration] = new(JsonSerializer.SerializeToElement(account), "==")
			},
			null);
		var context = await new VariableTemplateRenderer(new VariableRegistry())
			.CreateContextAsync(VariableScope.Global, null);

		return new EventSubscriptionMatcher(
				new ActionConditionEvaluator(new VariableTemplateRenderer(new VariableRegistry())))
			.Matches(subscription, descriptor, context.WithEvent(occurrence.Payload));
	}

	private SteamTracker Tracker(params SteamAccountConfig[] accounts)
	{
		return new SteamTracker(_platform,
			accounts,
			[],
			_time,
			_ =>
			{
			},
			_events.Add);
	}

	private static SteamAccountConfig Account(string entryId, ISteamWebApiClient client)
	{
		return new SteamAccountConfig(entryId, "Main", "main", SteamId, client);
	}

	private static SteamPlayerSummary Self(int persona, int? gameId = null, string? gameName = null)
	{
		return new SteamPlayerSummary(SteamId, "Me", persona, gameId, gameName);
	}

	private static SteamPlayerSummary Summary(string id, int persona, int? gameId = null)
	{
		return new SteamPlayerSummary(id, "Friend " + id, persona, gameId, gameId is null ? null : "Game " + gameId);
	}

	private static SteamFriend Friend(string id, int persona)
	{
		return new SteamFriend(id, "Friend " + id, persona, null, null, null);
	}
}
