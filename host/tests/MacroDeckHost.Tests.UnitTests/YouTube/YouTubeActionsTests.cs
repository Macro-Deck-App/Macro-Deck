using System.Net;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.YouTube;
using MacroDeckHost.Integrations.YouTube.Actions;
using MacroDeckHost.Integrations.YouTube.Protocol;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Twitch;

namespace MacroDeckHost.Tests.UnitTests.YouTube;

[TestFixture]
internal sealed class YouTubeActionsTests
{
	private const string ChannelId = "UCchannel";

	private FakeYouTubeApiClient _api = null!;
	private YouTubeAccountManager _accounts = null!;
	private IReadOnlyList<IActionDefinition> _actions = null!;

	[SetUp]
	public async Task SetUp()
	{
		_api = new FakeYouTubeApiClient();
		var config = new RecordingIntegrationConfig();
		YouTubeTestSupport.AddChannel(config, ChannelId, "Channel");
		_accounts = YouTubeTestSupport.Manager(_ => _api);
		await _accounts.ReloadAsync(config);
		_actions = YouTubeActions.Create(() => _accounts);
	}

	[TearDown]
	public void TearDown() => _accounts.Dispose();

	[Test]
	public void The_actions_are_offered_with_a_channel_picker()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_actions.Select(action => action.Id), Is.EqualTo(new[]
			{
				"send-chat-message", "set-title", "set-description", "set-tags", "start-ad-break", "go-live",
				"end-stream"
			}));
			Assert.That(_actions.Select(action => action.Parameters[0].Name), Is.All.EqualTo("account"));
		});
	}

	[Test]
	public async Task A_chat_message_goes_to_the_live_chat_of_the_live_broadcast()
	{
		_api.Active.Add(YouTubeTestSupport.Broadcast("b1", YouTubeLifeCycleStatus.Live, "chat-9"));

		var result = await Run("send-chat-message", ("message", "Hello chat"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_api.SentMessages, Is.EqualTo(new[] { ("chat-9", "Hello chat") }));
		});
	}

	[Test]
	public async Task A_chat_message_longer_than_youtube_allows_is_refused_before_sending()
	{
		_api.Active.Add(YouTubeTestSupport.Broadcast("b1", YouTubeLifeCycleStatus.Live, "chat-9"));

		var result = await Run("send-chat-message", ("message", new string('a', 201)));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(_api.SentMessages, Is.Empty);
		});
	}

	[Test]
	public async Task A_chat_message_while_not_live_explains_why_it_was_not_sent()
	{
		var result = await Run("send-chat-message", ("message", "Hello"));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("live"));
		});
	}

	[Test]
	public async Task Changing_the_title_sends_back_every_other_snippet_field()
	{
		_api.Active.Add(YouTubeTestSupport.Broadcast("b1", YouTubeLifeCycleStatus.Live, "chat-1"));
		_api.Videos["b1"] = YouTubeTestSupport.Video("b1", title: "Old title");

		var result = await Run("set-title", ("title", "New title"));

		var (videoId, snippet) = _api.Updates.Single();
		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(videoId, Is.EqualTo("b1"));
			Assert.That(snippet.Title, Is.EqualTo("New title"));
			Assert.That(snippet.CategoryId, Is.EqualTo("20"));
			Assert.That(snippet.Description, Is.EqualTo("An old description"));
			Assert.That(snippet.Tags, Is.EqualTo(new[] { "old", "tags" }));
			Assert.That(snippet.DefaultLanguage, Is.EqualTo("en"));
			Assert.That(snippet.DefaultAudioLanguage, Is.EqualTo("en-US"));
		});
	}

	[Test]
	public async Task Without_a_live_broadcast_the_next_scheduled_one_is_changed()
	{
		_api.Upcoming.Add(YouTubeTestSupport.Broadcast("later", YouTubeLifeCycleStatus.Ready,
			scheduledStart: YouTubeTestSupport.Now.AddDays(2)));
		_api.Upcoming.Add(YouTubeTestSupport.Broadcast("sooner", YouTubeLifeCycleStatus.Created,
			scheduledStart: YouTubeTestSupport.Now.AddDays(1)));
		_api.Videos["sooner"] = YouTubeTestSupport.Video("sooner");
		_api.Videos["later"] = YouTubeTestSupport.Video("later");

		await Run("set-description", ("description", "Fresh description"));

		var (videoId, snippet) = _api.Updates.Single();
		Assert.Multiple(() =>
		{
			Assert.That(videoId, Is.EqualTo("sooner"));
			Assert.That(snippet.Description, Is.EqualTo("Fresh description"));
			Assert.That(snippet.Title, Is.EqualTo("My stream"));
		});
	}

	[TestCase("")]
	[TestCase("A <b>bold</b> title")]
	public async Task A_title_youtube_would_refuse_is_rejected_without_a_request(string title)
	{
		_api.Active.Add(YouTubeTestSupport.Broadcast("b1", YouTubeLifeCycleStatus.Live));

		var result = await Run("set-title", ("title", title));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(_api.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task A_title_longer_than_one_hundred_characters_is_rejected()
	{
		var result = await Run("set-title", ("title", new string('t', 101)));

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
	}

	[Test]
	public async Task Tags_are_split_at_commas_and_line_breaks()
	{
		_api.Active.Add(YouTubeTestSupport.Broadcast("b1", YouTubeLifeCycleStatus.Live));
		_api.Videos["b1"] = YouTubeTestSupport.Video("b1");

		await Run("set-tags", ("tags", "speedrun, retro\nlive coding"));

		Assert.That(_api.Updates.Single().Snippet.Tags, Is.EqualTo(new[] { "speedrun", "retro", "live coding" }));
	}

	[Test]
	public async Task Tags_over_five_hundred_characters_are_rejected()
	{
		var tags = string.Join(',', Enumerable.Repeat(new string('x', 99), 6));

		var result = await Run("set-tags", ("tags", tags));

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
	}

	[Test]
	public async Task Changing_the_title_without_any_broadcast_says_so()
	{
		var result = await Run("set-title", ("title", "New"));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("broadcast"));
		});
	}

	[Test]
	public async Task Going_live_starts_the_first_ready_scheduled_broadcast()
	{
		_api.Upcoming.Add(YouTubeTestSupport.Broadcast("created", YouTubeLifeCycleStatus.Created,
			scheduledStart: YouTubeTestSupport.Now.AddMinutes(5)));
		_api.Upcoming.Add(YouTubeTestSupport.Broadcast("ready-later", YouTubeLifeCycleStatus.Ready,
			scheduledStart: YouTubeTestSupport.Now.AddHours(3)));
		_api.Upcoming.Add(YouTubeTestSupport.Broadcast("ready", YouTubeLifeCycleStatus.Ready,
			scheduledStart: YouTubeTestSupport.Now.AddHours(1)));

		var result = await Run("go-live");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_api.Transitions, Is.EqualTo(new[] { ("ready", YouTubeBroadcastTransition.Live) }));
		});
	}

	[Test]
	public async Task Going_live_without_a_ready_broadcast_says_what_is_missing()
	{
		_api.Upcoming.Add(YouTubeTestSupport.Broadcast("created", YouTubeLifeCycleStatus.Created));

		var result = await Run("go-live");

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("ready"));
			Assert.That(_api.Transitions, Is.Empty);
		});
	}

	[Test]
	public async Task A_broadcast_youtube_starts_by_itself_is_not_transitioned()
	{
		_api.Upcoming.Add(YouTubeTestSupport.Broadcast("auto", YouTubeLifeCycleStatus.Ready, enableAutoStart: true));

		var result = await Run("go-live");

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("by itself"));
			Assert.That(_api.Transitions, Is.Empty);
		});
	}

	[TestCase("errorStreamInactive", ActionErrorCodes.ProviderRejected, "encoder")]
	[TestCase("invalidTransition", ActionErrorCodes.ProviderRejected, "testing")]
	[TestCase("liveStreamingNotEnabled", ActionErrorCodes.PermissionDenied, "not enabled")]
	[TestCase("insufficientLivePermissions", ActionErrorCodes.PermissionDenied, "not allowed")]
	[TestCase("quotaExceeded", ActionErrorCodes.Unavailable, "quota")]
	[TestCase("userRequestsExceedRateLimit", ActionErrorCodes.ProviderRejected, "too many")]
	[TestCase("somethingElse", ActionErrorCodes.ProviderRejected, "rejected")]
	public async Task A_refused_transition_explains_the_reason(string reason, string code, string explanation)
	{
		_api.Upcoming.Add(YouTubeTestSupport.Broadcast("ready", YouTubeLifeCycleStatus.Ready));
		_api.Failures["transition"] = YouTubeTestSupport.ApiError(reason);

		var result = await Run("go-live");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(code));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain(explanation));
		});
	}

	[Test]
	public async Task A_redundant_transition_already_did_what_was_asked()
	{
		_api.Active.Add(YouTubeTestSupport.Broadcast("live", YouTubeLifeCycleStatus.Live));
		_api.Failures["transition"] = YouTubeTestSupport.ApiError("redundantTransition");

		var result = await Run("end-stream");

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
	}

	[Test]
	public async Task Ending_the_stream_completes_the_live_broadcast()
	{
		_api.Active.Add(YouTubeTestSupport.Broadcast("testing", YouTubeLifeCycleStatus.Testing));
		_api.Active.Add(YouTubeTestSupport.Broadcast("live", YouTubeLifeCycleStatus.Live));

		await Run("end-stream");

		Assert.That(_api.Transitions, Is.EqualTo(new[] { ("live", YouTubeBroadcastTransition.Complete) }));
	}

	[Test]
	public async Task An_ad_break_is_a_cuepoint_on_the_live_broadcast()
	{
		_api.Active.Add(YouTubeTestSupport.Broadcast("live", YouTubeLifeCycleStatus.Live));

		var result = await Run("start-ad-break", ("duration", "90"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_api.Cuepoints, Is.EqualTo(new[] { ("live", 90) }));
		});
	}

	[Test]
	public async Task An_ad_break_while_not_live_is_refused()
	{
		var result = await Run("start-ad-break", ("duration", "60"));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
			Assert.That(_api.Cuepoints, Is.Empty);
		});
	}

	[Test]
	public async Task A_used_up_quota_refuses_actions_without_calling_youtube()
	{
		_accounts.Connections[0].Budget.MarkExhausted();

		var result = await Run("end-stream");

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(_api.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task An_unknown_channel_is_reported()
	{
		var result = await Run("go-live", ("account", "UCgone"));

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConfigured));
	}

	[Test]
	public async Task An_expired_sign_in_asks_to_reconnect()
	{
		_api.Upcoming.Add(YouTubeTestSupport.Broadcast("ready", YouTubeLifeCycleStatus.Ready));
		_api.Failures["transition"] = YouTubeTestSupport.ApiError("authError", HttpStatusCode.Unauthorized);

		var result = await Run("go-live");

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.PermissionDenied));
	}

	[Test]
	public async Task Going_live_with_a_monitor_stream_runs_the_testing_phase_first()
	{
		await UseImmediateDelaysAsync();
		_api.Upcoming.Add(YouTubeTestSupport.Broadcast("monitored", YouTubeLifeCycleStatus.Ready,
			enableMonitorStream: true));
		_api.Active.Add(YouTubeTestSupport.Broadcast("monitored", YouTubeLifeCycleStatus.Testing,
			enableMonitorStream: true));

		var result = await Run("go-live");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_api.Transitions, Is.EqualTo(new[]
			{
				("monitored", YouTubeBroadcastTransition.Testing),
				("monitored", YouTubeBroadcastTransition.Live)
			}));
		});
	}

	[Test]
	public async Task Going_live_finds_the_testing_phase_when_youtube_still_lists_the_broadcast_as_upcoming()
	{
		await UseImmediateDelaysAsync();
		_api.Upcoming.Add(YouTubeTestSupport.Broadcast("monitored", YouTubeLifeCycleStatus.Ready,
			enableMonitorStream: true));
		_api.Transitioned = (id, status) =>
		{
			if (status == YouTubeBroadcastTransition.Testing)
			{
				_api.Upcoming[0] = YouTubeTestSupport.Broadcast(id, YouTubeLifeCycleStatus.Testing,
					enableMonitorStream: true);
			}
		};

		var result = await Run("go-live");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_api.Transitions.Last(), Is.EqualTo(("monitored", YouTubeBroadcastTransition.Live)));
		});
	}

	[Test]
	public async Task Going_live_stops_and_explains_when_the_testing_phase_does_not_start()
	{
		await UseImmediateDelaysAsync();
		_api.Upcoming.Add(YouTubeTestSupport.Broadcast("monitored", YouTubeLifeCycleStatus.Ready,
			enableMonitorStream: true));

		var result = await Run("go-live");

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("test phase"));
			Assert.That(_api.Transitions, Is.EqualTo(new[] { ("monitored", YouTubeBroadcastTransition.Testing) }));
		});
	}

	private async Task UseImmediateDelaysAsync()
	{
		_accounts.Dispose();
		var config = new RecordingIntegrationConfig();
		YouTubeTestSupport.AddChannel(config, ChannelId, "Channel");
		_accounts = YouTubeTestSupport.Manager(_ => _api,
			options: YouTubeTestSupport.ManualOptions() with { Delay = (_, _) => Task.CompletedTask });
		await _accounts.ReloadAsync(config);
	}

	private Task<ActionResult> Run(string id, params (string Name, object Value)[] parameters)
		=> _actions.Single(action => action.Id == id)
			.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = parameters.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal)
			});
}
