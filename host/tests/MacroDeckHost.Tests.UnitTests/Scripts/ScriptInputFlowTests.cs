using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Scripts;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Integrations.Scripts;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;
using SdkScript = MacroDeck.Sdk.Scripts.Script;

namespace MacroDeckHost.Tests.UnitTests.Scripts;

/// <summary>
/// A script run driven through the real flow executor and template renderer, which is the only place the
/// promise of the feature - a supplied input is readable as <c>vars.&lt;name&gt;</c> inside the running
/// script - is actually observable.
/// </summary>
[TestFixture]
public class ScriptInputFlowTests
{
	private static readonly string[] _general = ["general"];
	private static readonly string[] _generalThenAlerts = ["general", "alerts"];
	private static readonly string[] _liveThenStartingSoon = ["Live", "Starting Soon"];
	private static readonly string[] _ran = ["ran"];

	private static readonly Guid WInvoker = Guid.Parse("00000000-0000-0000-0000-00000000aaaa");
	private static readonly Guid WOther = Guid.Parse("00000000-0000-0000-0000-00000000bbbb");
	private static readonly Guid W1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
	private static readonly Guid W2 = Guid.Parse("00000000-0000-0000-0000-000000000002");

	private const string ChannelFlow =
		"""
		[
		  {
		    "triggerId": "onRun", "triggerType": "onRun",
		    "children": [
		      { "id": "b1", "type": "action", "blockType": "integration.capture",
		        "integrationId": "integration", "actionId": "capture",
		        "parameters": [{ "name": "marker", "type": "string", "value": "{{ vars.channel }}" }] }
		    ]
		  }
		]
		""";

	// The capture block inside a widget-enabled script's own flow - it declares no "marker" value of its
	// own; the assertion is on CapturingActionDefinition.CapturedOwnerWidgetId, i.e. whichever widget
	// FlowExecutionRequest.OwnerWidgetId carried into this run - exactly the value a real widget-targeting
	// block would resolve "$self" through, via the existing WidgetActionParameters.TargetOf.
	private const string TargetFlow =
		"""
		[
		  {
		    "triggerId": "onRun", "triggerType": "onRun",
		    "children": [
		      { "id": "b1", "type": "action", "blockType": "integration.capture",
		        "integrationId": "integration", "actionId": "capture",
		        "parameters": [{ "name": "marker", "type": "string", "value": "ran" }] }
		    ]
		  }
		]
		""";

	private const string SideEffectFlow =
		"""
		[
		  {
		    "triggerId": "onRun", "triggerType": "onRun",
		    "children": [
		      { "id": "b1", "type": "action", "blockType": "integration.capture",
		        "integrationId": "integration", "actionId": "capture",
		        "parameters": [{ "name": "marker", "type": "string", "value": "yes" }] }
		    ]
		  }
		]
		""";

	private List<string> _markers = null!;
	private VariableRegistry _variables = null!;
	private FlowExecutor _flowExecutor = null!;
	private ScriptCache _scripts = null!;
	private ScriptRunner _runner = null!;
	private DirectScriptApi _api = null!;

	[SetUp]
	public async Task SetUp()
	{
		_markers = [];
		var capture = new CapturingActionDefinition();
		capture.OnExecuted = () =>
			_markers.Add(capture.CapturedOwnerWidgetId ?? capture.CapturedParameters!["marker"].ToString()!);

		var integrations = new FakeIntegrationRegistry();
		integrations.Add(new FakeIntegration { Id = "integration", Actions = [capture] });
		integrations.Add(new FakeIntegration { Id = "scripts", Actions = [new RunScriptActionDefinition(() => _api)] });

		_variables = new VariableRegistry();
		var renderer = new VariableTemplateRenderer(_variables);
		_flowExecutor = new FlowExecutor(integrations,
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
			Log.Logger);

		_scripts = new ScriptCache(new InMemoryScriptStore(), new LoggerConfiguration().CreateLogger());
		await _scripts.InitializeCache();
		_runner = new ScriptRunner(_scripts,
			_flowExecutor,
			new FakeWidgetAppearanceService(WInvoker.ToString(), WOther.ToString(), W1.ToString(), W2.ToString()),
			new LoggerConfiguration().CreateLogger());
		_api = new DirectScriptApi(_runner, _scripts);
	}

	[TearDown]
	public void TearDown() => _scripts.Dispose();

	[Test]
	public async Task A_supplied_input_is_what_the_running_script_reads_as_vars_name()
	{
		var script = await Store(ChannelFlow, [Input("channel")]);

		var result = await _runner.RunAsync(script.Id,
			null,
			CancellationToken.None,
			0,
			Supplied(("channel", "general")));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
			Assert.That(_markers, Is.EqualTo(_general));
		});
	}

	[Test]
	public async Task Two_runs_of_the_same_script_read_their_own_supplied_value()
	{
		var script = await Store(ChannelFlow, [Input("channel")]);

		await _runner.RunAsync(script.Id, null, CancellationToken.None, 0, Supplied(("channel", "general")));
		await _runner.RunAsync(script.Id, null, CancellationToken.None, 0, Supplied(("channel", "alerts")));

		Assert.That(_markers, Is.EqualTo(_generalThenAlerts));
	}

	[Test]
	public async Task An_input_shadows_a_global_for_its_run_only_and_never_writes_it()
	{
		_variables.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = "channel",
			Scope = VariableScope.Global,
			Type = VariableType.Text,
			Classification = VariableClassification.User,
			Value = "Starting Soon"
		});
		var script = await Store(ChannelFlow, [Input("channel")]);

		await _runner.RunAsync(script.Id, null, CancellationToken.None, 0, Supplied(("channel", "Live")));
		await _runner.RunAsync(script.Id, null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_markers, Is.EqualTo(_liveThenStartingSoon));
			Assert.That(_variables.GetByScope(VariableScope.Global, null).Single().Value, Is.EqualTo("Starting Soon"));
		});
	}

	// Acceptance scenario 1 (regression - must fail if the fix is reverted): a widget's own flow runs a
	// script through a Run Script block whose widget target is the literal "$self"; the target script
	// (toggle on) reads its own owner widget back as the invoking widget's real id.
	[Test]
	public async Task Self_is_resolved_from_the_owner_widget_at_the_run_script_call_site()
	{
		var script = await Store(TargetFlow, runsOnWidget: true);

		var outerFlow = RunScriptFlow(script.Id);
		var result = await _flowExecutor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = outerFlow,
				Trigger = TriggerSelector.ByType("onShortPress"),
				Scope = VariableScope.Global,
				OwnerWidgetId = WInvoker
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
			// The exact single-element match below already rules out "$self" leaking through and rules
			// out WOther being touched - a separate `Has.None.EqualTo(...)` on either would be
			// unfalsifiable, since nothing in this test could ever put them in `_markers` in the first
			// place.
			Assert.That(_markers, Is.EqualTo(new[] { WInvoker.ToString() }));
		});
	}

	// Acceptance scenario 1b - the substitution is per call site, not ambient: a toggle-off script's own
	// Run Script call must not inherit whatever widget it happens to run from, because ScriptRunner
	// deliberately sets no OwnerWidgetId for a toggle-off script's own execution.
	[Test]
	public async Task A_toggle_off_scripts_nested_call_does_not_inherit_a_widget()
	{
		var inner = await Store(SideEffectFlow, runsOnWidget: true);
		var outer = await Store(RunScriptFlowArray(inner.Id));

		var result = await _runner.RunAsync(outer.Id, null, CancellationToken.None, 0, null, WInvoker.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Failed));
			Assert.That(result.Actions.Single().ErrorCode, Is.EqualTo(ActionExecutionErrorCodes.ScriptWidgetRequired));
			Assert.That(_markers, Is.Empty, "the inner script's side effect must not have run");
		});
	}

	// Acceptance scenario 1c - a widget-enabled script CAN pass its own widget down to a nested call.
	[Test]
	public async Task A_toggle_on_scripts_nested_call_receives_its_own_widget()
	{
		var inner = await Store(TargetFlow, runsOnWidget: true);
		var outer = await Store(RunScriptFlowArray(inner.Id), runsOnWidget: true);

		var result = await _runner.RunAsync(outer.Id, null, CancellationToken.None, 0, null, WInvoker.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
			Assert.That(_markers, Is.EqualTo(new[] { WInvoker.ToString() }));
		});
	}

	// Acceptance scenario 2: the same script, run for two different widgets, each affects only the widget
	// it was given.
	[Test]
	public async Task The_same_script_run_for_two_widgets_each_affects_only_its_own_widget()
	{
		var script = await Store(TargetFlow, runsOnWidget: true);

		var result1 = await _runner.RunAsync(script.Id, null, CancellationToken.None, 0, null, W1.ToString());
		var result2 = await _runner.RunAsync(script.Id, null, CancellationToken.None, 0, null, W2.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(result1.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
			Assert.That(result2.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
			Assert.That(_markers, Is.EqualTo(new[] { W1.ToString(), W2.ToString() }));
		});
	}

	// Acceptance scenario 3: a widget-enabled script with no owner widget fails before any block runs.
	[Test]
	public async Task A_widget_enabled_script_with_no_owner_widget_fails_with_script_widget_required()
	{
		var script = await Store(SideEffectFlow, runsOnWidget: true);

		var result = await _runner.RunAsync(script.Id, null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionExecutionErrorCodes.ScriptWidgetRequired));
			Assert.That(_markers, Is.Empty);
		});
	}

	// Acceptance scenario 4: every bad owner widget value fails the same way, and nothing runs.
	[TestCase("")]
	[TestCase("   ")]
	[TestCase("$self")]
	[TestCase(" $SELF ")]
	[TestCase("widget-that-does-not-exist")]
	public async Task A_bad_owner_widget_value_fails_with_script_widget_required(string value)
	{
		var script = await Store(SideEffectFlow, runsOnWidget: true);

		var result = await _runner.RunAsync(script.Id, null, CancellationToken.None, 0, null, value);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionExecutionErrorCodes.ScriptWidgetRequired));
			Assert.That(_markers, Is.Empty);
		});
	}

	// Acceptance scenario 5: a toggle-off script is unaffected - it runs with no owner widget from every
	// caller, including one that happens to supply one.
	[Test]
	public async Task A_toggle_off_script_runs_with_no_owner_widget_even_when_one_is_supplied()
	{
		var script = await Store(TargetFlow, runsOnWidget: false);

		var result = await _runner.RunAsync(script.Id, null, CancellationToken.None, 0, null, WInvoker.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
			Assert.That(_markers, Is.EqualTo(_ran));
		});
	}

	private static string RunScriptFlow(Guid scriptId)
		=> $$"""
			 { "flows": {{RunScriptFlowArray(scriptId, "onShortPress")}} }
			 """;

	// The bare-array shape ScriptEntity.Flows itself expects (see ChannelFlow/TargetFlow/SideEffectFlow) -
	// used when this Run Script block is the body of a script rather than a widget's own flow.
	private static string RunScriptFlowArray(Guid scriptId, string triggerType = "onRun")
		=> $$"""
			 [
			   {
			     "triggerId": "t1", "triggerType": "{{triggerType}}",
			     "children": [
			       { "id": "b1", "type": "action", "blockType": "scripts.run-script",
			         "integrationId": "scripts", "actionId": "run-script",
			         "parameters": [
			           { "name": "scriptId", "type": "string", "value": "{{scriptId}}" },
			           { "name": "widget", "type": "string", "value": "$self" }
			         ] }
			     ]
			   }
			 ]
			 """;

	private static ScriptInput Input(string name) => new() { Name = name, Type = ScriptInputType.Text };

	private static Dictionary<string, object?> Supplied(params (string Name, object? Value)[] values)
		=> values.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);

	private Task<ScriptEntity> Store(string flows, bool runsOnWidget = false)
		=> Store(flows, [], runsOnWidget);

	private async Task<ScriptEntity> Store(string flows, ScriptInput[] inputs, bool runsOnWidget = false)
	{
		var script = new ScriptEntity
		{
			Id = Guid.NewGuid(),
			Name = "Script",
			Flows = flows,
			Inputs = [.. inputs],
			RunsOnWidget = runsOnWidget,
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow
		};
		await _scripts.AddOrUpdate(script);
		return script;
	}

	private sealed class NullActionInteractions : IActionInteractions
	{
		public void RequestItemPicker(string? originClientId,
			string instanceId,
			MusicPlayerCatalogItemKind kind,
			string? prompt = null)
		{
		}

		public void RequestDevicePicker(string? originClientId,
			string instanceId,
			bool startPlayback,
			string? prompt = null)
		{
		}
	}

	// Serves RunScriptActionDefinition from the real ScriptRunner without the production ScriptApi's DI
	// scope machinery, so a Run Script block inside these tests' flows really executes the target script
	// rather than merely recording that it was asked to.
	private sealed class DirectScriptApi : MacroDeck.Sdk.Scripts.IScriptApi
	{
		private readonly ScriptRunner _runner;
		private readonly ScriptCache _scripts;

		public DirectScriptApi(ScriptRunner runner, ScriptCache scripts)
		{
			_runner = runner;
			_scripts = scripts;
		}

		public IReadOnlyList<SdkScript> GetScripts()
			=> _scripts.GetAll()
				.Select(script => new SdkScript
				{
					Id = script.Id.ToString(),
					Name = script.Name,
					Description = script.Description,
					Inputs = script.Inputs.Select(ScriptInputSdkMapper.ToSdk).ToList(),
					RunsOnWidget = script.RunsOnWidget
				})
				.ToList();

		public async Task<ActionResult> RunAsync(
			string scriptId,
			IReadOnlyDictionary<string, object?>? inputs = null,
			string? originClientId = null,
			string? ownerWidgetId = null,
			CancellationToken cancellationToken = default)
		{
			var result = await _runner.RunAsync(Guid.Parse(scriptId),
				originClientId,
				cancellationToken,
				0,
				inputs,
				ownerWidgetId);
			return result.Status == FlowExecutionStatus.Succeeded
				? ActionResult.Success()
				: ActionResult.Failed(result.ErrorCode ?? ActionExecutionErrorCodes.FlowError,
					result.ErrorMessage.IsEmpty ? "The script failed." : result.ErrorMessage);
		}
	}
}
