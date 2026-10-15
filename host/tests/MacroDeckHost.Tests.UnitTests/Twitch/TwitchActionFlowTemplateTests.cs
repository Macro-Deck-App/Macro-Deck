using System.Globalization;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Integrations.Twitch;
using MacroDeckHost.Integrations.Twitch.Actions;
using MacroDeckHost.Integrations.Twitch.Auth;
using MacroDeckHost.Integrations.Twitch.Configuration;
using MacroDeckHost.Integrations.Twitch.Connections;
using MacroDeckHost.Integrations.Twitch.Protocol;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Twitch;

[TestFixture]
internal sealed class TwitchActionFlowTemplateTests
{
	private const string Category = "{{ vars.stream_category }}";

	private FakeTwitchHelixClient _helix = null!;
	private TwitchAccountManager _accounts = null!;
	private FlowExecutor _executor = null!;

	[SetUp]
	public async Task SetUp()
	{
		_helix = new FakeTwitchHelixClient { User = new TwitchUserInfo("999", "target", "Target") };
		var config = new RecordingIntegrationConfig();
		config.AddEntry("Twitch (streamer)",
			new Dictionary<string, string?>(StringComparer.Ordinal)
			{
				[TwitchConfigKeys.ClientId] = "client-id",
				[TwitchConfigKeys.UserId] = "111",
				[TwitchConfigKeys.Login] = "streamer",
				[TwitchConfigKeys.DisplayName] = "streamer",
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
		var clients = new Queue<FakeTwitchHelixClient>([_helix]);
		_accounts = new TwitchAccountManager(() => new FakeTwitchOAuthClient(),
			new LoggerConfiguration().CreateLogger(),
			(_, _) => clients.Dequeue());
		await _accounts.ReloadAsync(config);

		var registry = new FakeIntegrationRegistry();
		registry.Add(new FakeIntegration
		{
			Id = TwitchIntegration.IntegrationId,
			Actions = TwitchActions.Create(() => _accounts, () => null)
		});

		var variables = new VariableRegistry();
		variables.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = "stream_category",
			Scope = VariableScope.Global,
			Type = VariableType.Text,
			Classification = VariableClassification.User,
			Value = "Minecraft"
		});
		var renderer = new VariableTemplateRenderer(variables);
		_executor = new FlowExecutor(registry,
			renderer,
			new ActionConditionEvaluator(renderer),
			new FakeSecretService(),
			new NullActionInteractions(),
			new NullUiInteractions(),
			new UserNotificationStore(),
			new MusicPlayerPollNudge(registry),
			new FakeHostLockState(),
			TestLocalization.Preferences,
			TestLocalization.Resolver,
			Log.Logger);
	}

	[TearDown]
	public void TearDown()
	{
		_accounts.Dispose();
	}

	[Test]
	public async Task A_stream_marker_description_resolves_a_variable_next_to_static_text()
	{
		await Run("create-stream-marker", Text("description", $"Game Change: {Category}"));

		Assert.That(_helix.Calls, Does.Contain("marker:Game Change: Minecraft"));
	}

	[Test]
	public async Task A_chat_message_keeps_its_line_breaks_and_resolves_the_reply_id()
	{
		await Run("send-chat-message",
			Text("message", $"Now playing {Category}\\nenjoy"),
			Text("replyToMessageId", "{{ vars.stream_category }}-1"));

		Assert.That(_helix.Calls, Does.Contain("chat:Now playing Minecraft\nenjoy|Minecraft-1"));
	}

	[Test]
	public async Task Stream_info_resolves_the_title_the_category_lookup_and_the_tag_list()
	{
		await Run("set-stream-info",
			Text("title", $"Playing {Category}"),
			Text("category", Category),
			Text("tags", $"{Category}, English"));

		Assert.That(_helix.Calls,
			Is.EqualTo(new[] { "resolveCategory:Minecraft", "modifyChannel:Playing Minecraft||Minecraft+English" }));
	}

	[Test]
	public async Task Poll_choices_are_resolved_before_they_are_split_into_lines()
	{
		await Run("create-poll", Text("title", "Next game"), Text("choices", $"{Category}\\nCreative"));

		Assert.That(_helix.Calls, Does.Contain("createPoll:Next game|Minecraft+Creative|60|"));
	}

	[Test]
	public async Task Prediction_outcomes_are_resolved_before_they_are_split_into_lines()
	{
		await Run("create-prediction", Text("title", "Who wins"), Text("outcomes", $"{Category}\\nCreative"));

		Assert.That(_helix.Calls, Does.Contain("createPrediction:Who wins|Minecraft+Creative|120"));
	}

	[Test]
	public async Task Identifier_and_reason_fields_resolve_variables()
	{
		await Run("delete-chat-message", Text("messageId", "id-{{ vars.stream_category }}"));
		await Run("ban-user", Text("targetLogin", "target"), Text("reason", $"Spam in {Category}"));

		Assert.That(_helix.Calls, Does.Contain("deleteChat:id-Minecraft"));
		Assert.That(_helix.Calls, Does.Contain("ban:999||Spam in Minecraft"));
	}

	[Test]
	public async Task A_login_field_is_resolved_and_then_normalised()
	{
		await Run("send-shoutout", Text("targetLogin", "@Prefix-{{ vars.stream_category }}"));

		Assert.That(_helix.LastLookedUpLogin, Is.EqualTo("prefix-minecraft"));
		Assert.That(_helix.Calls, Does.Contain("shoutout:999"));
	}

	[Test]
	public async Task Static_text_is_sent_unchanged()
	{
		await Run("create-stream-marker", Text("description", "Plain marker"));

		Assert.That(_helix.Calls, Does.Contain("marker:Plain marker"));
	}

	[Test]
	public async Task A_predefined_choice_is_never_treated_as_a_template()
	{
		await Run("send-announcement",
			Text("message", "Hello"),
			$$"""{ "name": "color", "type": "choice", "value": "{{Category}}" }""");

		Assert.That(_helix.Calls, Does.Contain("announce:Hello|{{ vars.stream_category }}"));
	}

	private static string Text(string name, string value)
	{
		return $$"""{ "name": "{{name}}", "type": "string", "value": "{{value}}" }""";
	}

	private async Task Run(string actionId, params string[] parameters)
	{
		var flow = $$"""
					 { "flows": [ { "triggerId": "t1", "triggerType": "onShortPress", "children": [
					   { "id": "b1", "type": "action", "blockType": "{{TwitchIntegration.IntegrationId}}.{{actionId}}",
					     "integrationId": "{{TwitchIntegration.IntegrationId}}", "actionId": "{{actionId}}",
					     "parameters": [ {{string.Join(",", parameters)}} ] } ] } ] }
					 """;

		var result = await _executor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = flow,
				Trigger = TriggerSelector.ByType("onShortPress"),
				Scope = VariableScope.Global
			},
			CancellationToken.None);

		Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Succeeded), result.ErrorCode);
	}

	private sealed class NullActionInteractions : IActionInteractions
	{
		public void RequestItemPicker(
			string? originClientId,
			string instanceId,
			MusicPlayerCatalogItemKind kind,
			string? prompt = null)
		{
		}

		public void RequestDevicePicker(
			string? originClientId,
			string instanceId,
			bool startPlayback,
			string? prompt = null)
		{
		}
	}
}
