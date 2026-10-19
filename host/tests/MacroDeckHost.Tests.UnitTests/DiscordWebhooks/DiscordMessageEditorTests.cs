using System.Text.Json;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Integrations.DiscordWebhooks.Actions;
using MacroDeckHost.Integrations.DiscordWebhooks.Configuration;
using MacroDeckHost.Integrations.DiscordWebhooks.Protocol;
using MacroDeckHost.Tests.UnitTests.Delegation;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.DiscordWebhooks;

[TestFixture]
internal sealed class DiscordMessageEditorTests
{
	private readonly List<IReadOnlyDictionary<string, JsonElement>> _reports = [];
	private RecordingIntegrationConfig _config = null!;
	private ScriptedDiscordHandler _discord = null!;
	private HttpClient _http = null!;
	private IActionConfigurationSession _session = null!;

	[SetUp]
	public async Task SetUp()
	{
		_reports.Clear();
		_config = new RecordingIntegrationConfig();
		var entry = _config.AddEntry("Announcements",
			new Dictionary<string, string?>(),
			new Dictionary<string, string> { [DiscordWebhooksConfigKeys.Url] = DiscordWebhookTestSupport.Url });
		_discord = new ScriptedDiscordHandler();
		_http = new HttpClient(_discord, false);

		var definition = new SendMessageActionDefinition(new DiscordWebhookTargets(() => _config),
			new DiscordWebhookSender(() => _http, (_, _) => Task.CompletedTask),
			new FixedTimeProvider(DateTimeOffset.UnixEpoch),
			DiscordWebhookTestSupport.CapturingLogger(new List<LogEvent>()));

		var session = await definition.CreateConfigurationSessionAsync(new ActionConfigurationRequest
			{
				Session = new UiSessionRequest
				{
					Surface = new UiSurface { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
					UiModelVersion = 1
				},
				Parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
				{
					[SendMessageActionDefinition.WebhookParameter] =
						JsonSerializer.SerializeToElement(entry.ToString()),
					[SendMessageActionDefinition.MessageParameter] = JsonSerializer.SerializeToElement(
						new Dictionary<string, string> { ["content"] = "Hello" })
				}
			},
			CancellationToken.None);

		_session = (IActionConfigurationSession)session!;
		_session.ParameterValuesReported += (_, args) => _reports.Add(args.Values);
	}

	[TearDown]
	public async Task TearDown()
	{
		await _session.DisposeAsync();
		_http.Dispose();
		_discord.Dispose();
	}

	private static IEnumerable<UiNode> Nodes(UiNode node)
	{
		yield return node;
		foreach (var child in node.Children.SelectMany(Nodes))
		{
			yield return child;
		}
	}

	private UiNode Node(Func<string, bool> id)
	{
		return Nodes(_session.BuildTree().Root).Single(node => id(node.Id));
	}

	private void Raise(string nodeId, string name, object? data = null)
	{
		_session.Dispatch(new UiEvent
		{
			NodeId = nodeId, Name = name, Data = data is null ? null : JsonSerializer.SerializeToElement(data)
		});
		_session.DrainPatches();
	}

	[Test]
	public void An_edit_inside_an_embed_reports_the_whole_message_parameter()
	{
		Raise("embeds", UiConfigEvents.Add);
		var title = Node(id => id.StartsWith("embeds.", StringComparison.Ordinal)
			&& id.EndsWith(".title", StringComparison.Ordinal));

		Raise(title.Id, UiConfigEvents.Change, "Going live");

		var message = _reports.Last()[SendMessageActionDefinition.MessageParameter];
		Assert.Multiple(() =>
		{
			Assert.That(message.GetProperty("content").GetString(), Is.EqualTo("Hello"));
			Assert.That(message.GetProperty("embeds.0.title").GetString(), Is.EqualTo("Going live"));
			Assert.That(Nodes(_session.BuildTree().Root).Select(node => node.Id),
				Has.Some.EndsWith(".preview.e0.title"),
				"the preview shows the new embed");
			Assert.That(_reports.SelectMany(report => report.Keys).Distinct(),
				Is.EqualTo(new[] { SendMessageActionDefinition.MessageParameter }),
				"only the declared message parameter is reported, never the webhook URL");
		});
	}

	[Test]
	public void The_first_test_send_button_only_asks_and_sends_nothing()
	{
		Raise(Node(id => id.EndsWith(".test-send-start", StringComparison.Ordinal)).Id, UiConfigEvents.Activate);

		Assert.Multiple(() =>
		{
			Assert.That(_discord.Requests, Is.Empty);
			Assert.That(Nodes(_session.BuildTree().Root).Select(node => node.Id),
				Has.Some.EndsWith("test-send-now"));
		});
	}

	[Test]
	public async Task Send_now_after_arming_sends_the_message_to_the_saved_webhook()
	{
		Raise(Node(id => id.EndsWith(".test-send-start", StringComparison.Ordinal)).Id, UiConfigEvents.Activate);
		var sendNow = Node(id => id.EndsWith("test-send-now", StringComparison.Ordinal));

		Raise(sendNow.Id, UiConfigEvents.Activate);
		await WaitForAsync(() => _discord.Requests.Count == 1);

		Assert.That(_discord.Requests.Single().Uri.AbsoluteUri, Is.EqualTo(DiscordWebhookTestSupport.Url));
	}

	[Test]
	public void An_edit_disarms_the_test_send()
	{
		Raise(Node(id => id.EndsWith(".test-send-start", StringComparison.Ordinal)).Id, UiConfigEvents.Activate);

		Raise("content", UiConfigEvents.Change, "Changed");

		Assert.That(Nodes(_session.BuildTree().Root).Select(node => node.Id), Has.None.EndsWith("test-send-now"));
	}

	[Test]
	public void The_preview_never_carries_the_webhook_url()
	{
		var tree = JsonSerializer.Serialize(_session.BuildTree());

		Assert.That(tree, Does.Not.Contain(DiscordWebhookTestSupport.Token));
	}

	private static async Task WaitForAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition() && DateTime.UtcNow < deadline)
		{
			await Task.Delay(10);
		}
	}
}
