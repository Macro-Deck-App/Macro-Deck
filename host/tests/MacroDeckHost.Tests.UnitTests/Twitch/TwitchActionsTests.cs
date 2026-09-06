using System.Globalization;
using MacroDeckHost.Integrations.Twitch;
using MacroDeckHost.Integrations.Twitch.Actions;
using MacroDeckHost.Integrations.Twitch.Auth;
using MacroDeckHost.Integrations.Twitch.Protocol;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using Serilog;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Twitch;

[TestFixture]
internal sealed class TwitchActionsTests
{
	private static readonly string[] _bothUserIds = ["111", "222"];

	private FakeTwitchHelixClient _first = null!;
	private FakeTwitchHelixClient _second = null!;
	private RecordingIntegrationConfig _config = null!;
	private TwitchAccountManager _accounts = null!;
	private IReadOnlyList<IActionDefinition> _actions = null!;

	[SetUp]
	public async Task SetUp()
	{
		_first = new FakeTwitchHelixClient();
		_second = new FakeTwitchHelixClient();
		_config = new RecordingIntegrationConfig();

		AddAccount("111", "streamer");
		AddAccount("222", "botaccount");

		var clients = new Queue<FakeTwitchHelixClient>([_first, _second]);
		_accounts = new TwitchAccountManager(() => new FakeTwitchOAuthClient(),
			SilentLogger(),
			(_, _) => clients.Dequeue());

		await _accounts.ReloadAsync(_config);
		_actions = TwitchActions.Create(() => _accounts, () => null);
	}

	[TearDown]
	public void TearDown()
	{
		_accounts.Dispose();
	}

	[Test]
	public async Task The_chat_mode_action_reports_whether_the_configured_mode_is_on()
	{
		_accounts.Resolve("111")!.Merge(state => state with
		{
			ChatSettings = new TwitchChatSettings(EmoteOnly: true,
				FollowersOnly: false,
				FollowersOnlyDurationMinutes: null,
				SlowMode: null,
				SlowModeWaitSeconds: null,
				SubscriberOnly: null,
				UniqueChat: null)
		});
		var provider = ChatModeProvider();

		var emoteOnly = await provider.GetActionStateAsync(ChatMode("111", "emote-only"), CancellationToken.None);
		var followersOnly =
			await provider.GetActionStateAsync(ChatMode("111", "followers-only"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(emoteOnly!.ActiveStateId, Is.EqualTo("on"));
			Assert.That(followersOnly!.ActiveStateId, Is.EqualTo("off"));
		});
	}

	[Test]
	public async Task The_chat_mode_action_is_unavailable_for_a_mode_twitch_has_not_reported()
	{
		var provider = ChatModeProvider();

		var snapshot = await provider.GetActionStateAsync(ChatMode("111", "slow"), CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("unavailable"));
	}

	[Test]
	public async Task The_chat_mode_action_reports_no_state_before_a_mode_is_chosen()
	{
		var provider = ChatModeProvider();

		var snapshot = await provider.GetActionStateAsync(
			new Dictionary<string, object?> { [TwitchActionDefinition.AccountParameterName] = "111" },
			CancellationToken.None);

		Assert.That(snapshot, Is.Null);
	}

	private IStateProviderActionDefinition ChatModeProvider()
		=> (IStateProviderActionDefinition)_actions.Single(action => action.Id == "set-chat-mode");

	private static Dictionary<string, object?> ChatMode(string accountId, string mode)
		=> new(StringComparer.Ordinal)
		{
			[TwitchActionDefinition.AccountParameterName] = accountId,
			[TwitchChatModeActionDefinition.ModeParameterName] = mode
		};

	[Test]
	public void Every_action_is_identified_and_takes_an_account()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_actions.Select(action => action.Id), Is.Unique);

			foreach (var action in _actions)
			{
				Assert.That(action.Id, Is.Not.Empty);
				Assert.That(TestLocalization.Resolve(action.Name), Is.Not.Empty);
				Assert.That(TestLocalization.Resolve(action.Description), Is.Not.Empty);
				Assert.That(action.Parameters[0].Name, Is.EqualTo("account"), action.Id);
				Assert.That(TestLocalization.Resolve(action.Parameters[0].Placeholder),
					Is.EqualTo("First account"),
					action.Id);
			}
		});
	}

	[Test]
	public void Every_action_parameter_uses_a_defined_type()
	{
		Assert.Multiple(() =>
		{
			foreach (var action in _actions)
			{
				foreach (var parameter in action.Parameters)
				{
					Assert.That(Enum.IsDefined(parameter.Type), Is.True, $"{action.Id}.{parameter.Name}");
				}
			}
		});
	}

	[Test]
	public async Task An_empty_account_acts_as_the_first_one()
	{
		await Run("send-chat-message", ("message", "hello"));

		Assert.Multiple(() =>
		{
			Assert.That(_first.Calls, Does.Contain("chat:hello|"));
			Assert.That(_second.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task A_named_account_acts_as_that_one()
	{
		await Run("send-chat-message", ("account", "222"), ("message", "hello"));

		Assert.Multiple(() =>
		{
			Assert.That(_second.Calls, Does.Contain("chat:hello|"));
			Assert.That(_first.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task An_unknown_account_is_skipped_and_reported_as_ACCOUNT_NOT_FOUND()
	{
		var result = await Run("send-chat-message", ("account", "nope"), ("message", "hello"));

		Assert.Multiple(() =>
		{
			Assert.That(_first.Calls, Is.Empty);
			Assert.That(_second.Calls, Is.Empty);
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo("ACCOUNT_NOT_FOUND"));
		});
	}

	[Test]
	public async Task A_failing_call_does_not_take_the_flow_down_but_is_reported_as_a_failure()
	{
		_first.FailingReads.Add("user");

		var result = await Run("start-raid", ("targetLogin", "other"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderError));
		});
	}

	[Test]
	public async Task A_call_Twitch_refuses_for_a_missing_scope_is_PERMISSION_DENIED()
	{
		_first.FailingReads.Add("user");
		_first.FailingReadExceptions["user"] = new TwitchScopeException("missing scope");

		var result = await Run("start-raid", ("targetLogin", "other"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.PermissionDenied));
		});
	}

	[Test]
	public async Task Setting_the_stream_info_resolves_the_category_and_omits_empty_fields()
	{
		_first.CategoryId = "509658";

		await Run("set-stream-info", ("title", "Playing Hades"), ("category", "Hades"), ("tags", "deutsch, roguelike"));

		Assert.Multiple(() =>
		{
			Assert.That(_first.Calls, Does.Contain("resolveCategory:Hades"));
			Assert.That(_first.Calls, Does.Contain("modifyChannel:Playing Hades|509658|deutsch+roguelike"));
		});
	}

	[Test]
	public async Task Setting_only_the_title_leaves_the_category_alone()
	{
		await Run("set-stream-info", ("title", "Playing Hades"));

		Assert.Multiple(() =>
		{
			Assert.That(_first.Calls, Does.Not.Contain("resolveCategory:"));
			Assert.That(_first.Calls, Does.Contain("modifyChannel:Playing Hades||"));
		});
	}

	[TestCase("@Raider")]
	[TestCase("https://twitch.tv/Raider")]
	[TestCase("Raider")]
	public async Task A_channel_name_is_accepted_as_a_mention_or_a_link(string entered)
	{
		_first.User = new TwitchUserInfo("999", "raider", "Raider");

		await Run("start-raid", ("targetLogin", entered));

		Assert.That(_first.Calls, Does.Contain("raid:999"));
	}

	[Test]
	public async Task A_timeout_and_a_permanent_ban_are_the_same_action()
	{
		_first.User = new TwitchUserInfo("999", "troll", "Troll");

		await Run("ban-user", ("targetLogin", "troll"), ("durationSeconds", 600), ("reason", "spam"));
		await Run("ban-user", ("targetLogin", "troll"));

		Assert.Multiple(() =>
		{
			Assert.That(_first.Calls, Does.Contain("ban:999|600|spam"));
			Assert.That(_first.Calls, Does.Contain("ban:999||"), "no duration means a permanent ban");
		});
	}

	[Test]
	public async Task Each_chat_mode_maps_onto_the_right_setting()
	{
		await Run("set-chat-mode", ("mode", "followers-only"), ("enabled", true), ("duration", 30));
		await Run("set-chat-mode", ("mode", "slow"), ("enabled", true), ("duration", 10));
		await Run("set-chat-mode", ("mode", "unique-chat"), ("enabled", false));

		Assert.Multiple(() =>
		{
			Assert.That(_first.Calls, Does.Contain("chatMode:FollowersOnly|True|30"));
			Assert.That(_first.Calls, Does.Contain("chatMode:SlowMode|True|10"));
			Assert.That(_first.Calls, Does.Contain("chatMode:UniqueChat|False|"));
		});
	}

	[Test]
	public async Task Clearing_chat_deletes_every_message_rather_than_one()
	{
		await Run("clear-chat");

		Assert.That(_first.Calls, Does.Contain("deleteChat:"));
	}

	[Test]
	public async Task A_poll_takes_its_choices_one_per_line()
	{
		await Run("create-poll",
			("title", "Next game"),
			("choices", "Elden Ring\nHades\n"),
			("durationSeconds", 120));

		Assert.That(_first.Calls, Does.Contain("createPoll:Next game|Elden Ring+Hades|120|"));
	}

	[Test]
	public async Task A_poll_with_one_choice_is_not_sent()
	{
		await Run("create-poll", ("title", "Next game"), ("choices", "Elden Ring"));

		Assert.That(_first.Calls, Is.Empty);
	}

	[Test]
	public async Task Ending_a_poll_finds_the_running_one_itself()
	{
		_first.ActivePoll = new TwitchActiveEvent("poll-1", "Next game", []);

		await Run("end-poll");

		Assert.That(_first.Calls, Does.Contain("endPoll:poll-1|False"));
	}

	[Test]
	public async Task Ending_a_poll_with_none_running_does_nothing()
	{
		await Run("end-poll");

		Assert.That(_first.Calls, Does.Not.Contain("endPoll:"));
	}

	[Test]
	public async Task Resolving_a_prediction_translates_the_outcome_title_into_its_id()
	{
		_first.ActivePrediction = new TwitchActiveEvent("p1",
			"Will we win",
			[new TwitchNamedOutcome("o1", "Yes"), new TwitchNamedOutcome("o2", "No")]);

		await Run("end-prediction", ("status", "RESOLVED"), ("winningOutcome", "no"));

		Assert.That(_first.Calls, Does.Contain("endPrediction:p1|RESOLVED|o2"));
	}

	[Test]
	public async Task A_clip_writes_its_edit_url_into_the_named_variable()
	{
		var variables = new RecordingVariableApi();
		var actions = TwitchActions.Create(() => _accounts, () => variables, InstantDelay);
		_first.ClipEditUrl = "https://clips.twitch.tv/edit/abc";

		await Run("create-clip", actions, ("targetVariable", "last_clip"));

		Assert.Multiple(() =>
		{
			Assert.That(_first.Calls, Does.Contain("clip:False"));
			Assert.That(variables.Written["last_clip"], Is.EqualTo("https://clips.twitch.tv/edit/abc"));
		});
	}

	[Test]
	public async Task A_clip_without_a_target_variable_writes_nothing()
	{
		var variables = new RecordingVariableApi();
		var actions = TwitchActions.Create(() => _accounts, () => variables, InstantDelay);

		await Run("create-clip", actions);

		Assert.Multiple(() =>
		{
			Assert.That(_first.Calls, Does.Contain("clip:False"));
			Assert.That(variables.Written, Is.Empty);
		});
	}

	[Test]
	public async Task A_clip_confirmed_on_the_third_poll_succeeds_and_writes_the_variable()
	{
		var variables = new RecordingVariableApi();
		var actions = TwitchActions.Create(() => _accounts, () => variables, InstantDelay);
		_first.ClipConfirmedAfterPolls = 3;

		var result = await Run("create-clip", actions, ("targetVariable", "last_clip"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_first.ClipPollCount, Is.EqualTo(3));
			Assert.That(variables.Written["last_clip"], Is.EqualTo(_first.ClipEditUrl));
		});
	}

	[Test]
	public async Task A_clip_that_never_confirms_is_CLIP_NOT_CONFIRMED_and_writes_nothing()
	{
		var variables = new RecordingVariableApi();
		var actions = TwitchActions.Create(() => _accounts, () => variables, InstantDelay);
		_first.ClipConfirmedAfterPolls = int.MaxValue;

		var result = await Run("create-clip", actions, ("targetVariable", "last_clip"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo("CLIP_NOT_CONFIRMED"));
			Assert.That(variables.Written, Is.Empty);
		});
	}

	[Test]
	public async Task HasDelay_reaches_the_create_clip_request()
	{
		var actions = TwitchActions.Create(() => _accounts, () => null, InstantDelay);

		await Run("create-clip", actions, ("hasDelay", true));

		Assert.That(_first.Calls, Does.Contain("clip:True"));
	}

	[Test]
	public async Task A_clip_Twitch_declines_to_create_is_CLIP_NOT_CREATED()
	{
		var actions = TwitchActions.Create(() => _accounts, () => null, InstantDelay);
		_first.ClipId = null;

		var result = await Run("create-clip", actions, ("targetVariable", "last_clip"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo("CLIP_NOT_CREATED"));
		});
	}

	[Test]
	public async Task A_clip_whose_every_confirmation_poll_fails_is_a_provider_error()
	{
		var variables = new RecordingVariableApi();
		var actions = TwitchActions.Create(() => _accounts, () => variables, InstantDelay);
		_first.FailingReads.Add("getClips");

		var result = await Run("create-clip", actions, ("targetVariable", "last_clip"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderError));
			Assert.That(variables.Written, Is.Empty);
		});
	}

	private static Task InstantDelay(TimeSpan duration, CancellationToken cancellationToken) => Task.CompletedTask;

	[Test]
	public async Task The_account_picker_lists_every_configured_account()
	{
		var action = (IDynamicOptionsActionDefinition)_actions.First(a => a.Id == "send-chat-message");

		var options = await action.GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = "account",
				CurrentParameters = new Dictionary<string, object?>(StringComparer.Ordinal)
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(options.Options.Select(option => option.Value), Is.EqualTo(_bothUserIds));
			Assert.That(options.AllowsCustomValue, Is.True);
		});
	}

	[Test]
	public async Task The_reward_picker_answers_from_the_cache_for_the_chosen_account()
	{
		var action = (IDynamicOptionsActionDefinition)_actions.First(a => a.Id == "set-redemption-status");

		var options = await action.GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = "rewardId",
				CurrentParameters = new Dictionary<string, object?>(StringComparer.Ordinal) { ["account"] = "222" }
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(options.Options, Is.Empty, "nothing has been polled yet");
			Assert.That(_second.Calls, Is.Empty, "the picker must not reach for the network");
		});
	}

	private Task<ActionResult> Run(string actionId, params (string Name, object Value)[] parameters)
		=> Run(actionId, _actions, parameters);

	private static Task<ActionResult> Run(
		string actionId,
		IReadOnlyList<IActionDefinition> actions,
		params (string Name, object Value)[] parameters)
	{
		var definition = actions.First(action => string.Equals(action.Id, actionId, StringComparison.Ordinal));

		return definition.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = parameters.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal)
			});
	}

	private void AddAccount(string userId, string login)
		=> _config.AddEntry($"Twitch ({login})",
			new Dictionary<string, string?>(StringComparer.Ordinal)
			{
				[TwitchConfigKeys.ClientId] = "client-id",
				[TwitchConfigKeys.UserId] = userId,
				[TwitchConfigKeys.Login] = login,
				[TwitchConfigKeys.DisplayName] = login,
				[TwitchConfigKeys.Scopes] = TwitchScopes.Requested,
				[TwitchConfigKeys.ConnectedAt] = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
				[TwitchConfigKeys.ExpiresAt] =
					DateTimeOffset.UtcNow.AddHours(4).ToString("o", CultureInfo.InvariantCulture)
			},
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				[TwitchConfigKeys.AccessToken] = "access",
				[TwitchConfigKeys.RefreshToken] = "refresh"
			});

	private static Logger SilentLogger() => new LoggerConfiguration().CreateLogger();
}
