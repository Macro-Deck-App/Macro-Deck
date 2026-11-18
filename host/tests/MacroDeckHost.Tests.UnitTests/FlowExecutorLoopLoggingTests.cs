using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests;

[TestFixture]
public class FlowExecutorLoopLoggingTests
{
	private List<LogEvent> _events = null!;
	private CapturingActionDefinition _capture = null!;
	private FlowExecutor _executor = null!;

	[SetUp]
	public void SetUp()
	{
		_events = [];
		_capture = new CapturingActionDefinition();

		var integrations = new FakeIntegrationRegistry();
		integrations.Add(new FakeIntegration { Id = "integration", Actions = [_capture] });

		var renderer = new VariableTemplateRenderer(new VariableRegistry());
		_executor = new FlowExecutor(integrations,
			renderer,
			new ActionConditionEvaluator(renderer),
			new FakeSecretService(),
			new NullActionInteractions(),
			new NullUiInteractions(),
			new UserNotificationStore(),
			new MusicPlayerPollNudge(integrations),
			new FakeHostLockState(),
			TestLocalization.Preferences,
			TestLocalization.Resolver,
			new LoggerConfiguration().WriteTo.Sink(new DelegatingLogSink(_events.Add)).CreateLogger());
	}

	[Test]
	public async Task A_Step_That_Cannot_Run_Is_Logged_Once_However_Often_Its_Loop_Repeats_It()
	{
		var result = await Run(Repeat(25,
			"""
			{ "id": "gone", "type": "action", "blockType": "removed.do-something",
			  "integrationId": "removed", "actionId": "do-something", "parameters": [] }
			"""));

		Assert.Multiple(() =>
		{
			Assert.That(_events.Count(logged => logged.Level == LogEventLevel.Warning), Is.EqualTo(1));
			Assert.That(result.Actions.Count(action => action.BlockId == "gone"), Is.EqualTo(25));
		});
	}

	[Test]
	public async Task A_Step_That_Throws_Is_Logged_Once_With_Its_Exception_However_Often_Its_Loop_Repeats_It()
	{
		_capture.Throw = new InvalidOperationException("device unplugged");

		var result = await Run(Repeat(25,
			"""
			{ "id": "broken", "type": "action", "blockType": "integration.capture",
			  "integrationId": "integration", "actionId": "capture", "parameters": [] }
			"""));

		var errors = _events.Where(logged => logged.Level == LogEventLevel.Error).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(errors, Has.Count.EqualTo(1));
			Assert.That(errors[0].Exception?.Message, Is.EqualTo("device unplugged"));
			Assert.That(_capture.ExecuteCount, Is.EqualTo(25));
			Assert.That(result.Actions.Count(action => action.BlockId == "broken"), Is.EqualTo(25));
		});
	}

	[Test]
	public async Task Two_Different_Steps_That_Cannot_Run_Are_Each_Logged()
	{
		await Run(Repeat(5,
			"""
			{ "id": "gone-a", "type": "action", "blockType": "removed.one",
			  "integrationId": "removed", "actionId": "one", "parameters": [] }
			""",
			"""
			{ "id": "gone-b", "type": "action", "blockType": "removed.two",
			  "integrationId": "removed", "actionId": "two", "parameters": [] }
			"""));

		Assert.That(_events.Count(logged => logged.Level == LogEventLevel.Warning), Is.EqualTo(2));
	}

	private Task<FlowExecutionResult> Run(string flowsSource)
	{
		return _executor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = flowsSource,
				Trigger = TriggerSelector.ById("t1")
			},
			CancellationToken.None);
	}

	private static string Repeat(int count, params string[] blocks)
	{
		return $$"""
				 {
				   "flows": [
				     { "triggerId": "t1", "triggerType": "onShortPress", "children": [
				       { "id": "loop", "type": "loop", "blockType": "repeatLoop",
				         "parameters": [{ "name": "count", "type": "number", "value": {{count}} }],
				         "children": [ {{string.Join(",", blocks)}} ] }
				     ] }
				   ]
				 }
				 """;
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
