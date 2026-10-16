using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Scripts;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Integrations.Scripts.Actions;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Scripts;
using Serilog;
using SdkScript = MacroDeck.Sdk.Scripts.Script;

namespace MacroDeckHost.Tests.UnitTests.Scripts;

[TestFixture]
public class ScriptLocalVariableFlowTests
{
	private static readonly Guid _widget = Guid.Parse("00000000-0000-0000-0000-00000000aaaa");

	private const string ReadXFlow =
		"""
		[
		  {
		    "triggerId": "onRun", "triggerType": "onRun",
		    "children": [
		      { "id": "b1", "type": "action", "blockType": "integration.capture",
		        "integrationId": "integration", "actionId": "capture",
		        "parameters": [{ "name": "marker", "type": "string", "value": "{{ vars.x }}" }] }
		    ]
		  }
		]
		""";

	private List<string> _markers = null!;
	private CapturingActionDefinition _capture = null!;
	private VariableRegistry _variables = null!;
	private FlowExecutor _flowExecutor = null!;
	private ScriptCache _scripts = null!;
	private ScriptRunner _runner = null!;
	private DirectScriptApi _api = null!;

	[SetUp]
	public async Task SetUp()
	{
		_markers = [];
		_capture = new CapturingActionDefinition();
		_capture.OnExecuted = () => _markers.Add(_capture.CapturedParameters!["marker"].ToString()!);

		var integrations = new FakeIntegrationRegistry();
		integrations.Add(new FakeIntegration { Id = "integration", Actions = [_capture] });
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
			new FakeWidgetAppearanceService(_widget.ToString()),
			new LoggerConfiguration().CreateLogger());
		_api = new DirectScriptApi(_runner, _scripts);
	}

	[TearDown]
	public void TearDown()
	{
		_scripts.Dispose();
	}

	[Test]
	public async Task A_script_reads_its_own_local_over_its_widgets_local_and_a_global()
	{
		var script = await Store(ReadXFlow, true);
		Seed(VariableScope.Global, null, "global");
		Seed(VariableScope.Widget, _widget.ToString(), "widget");
		Seed(VariableScope.Script, script.Id.ToString(), "script");

		await _runner.RunAsync(script.Id, null, CancellationToken.None, 0, null, _widget.ToString());

		Assert.That(_markers, Is.EqualTo(new[] { "script" }));
	}

	[Test]
	public async Task A_widget_enabled_script_without_a_local_reads_its_widgets_local_over_a_global()
	{
		var script = await Store(ReadXFlow, true);
		Seed(VariableScope.Global, null, "global");
		Seed(VariableScope.Widget, _widget.ToString(), "widget");

		await _runner.RunAsync(script.Id, null, CancellationToken.None, 0, null, _widget.ToString());

		Assert.That(_markers, Is.EqualTo(new[] { "widget" }));
	}

	[Test]
	public async Task A_called_script_does_not_see_its_callers_locals()
	{
		var inner = await Store(ReadXFlow);
		var outer = await Store(RunScriptFlow(inner.Id));
		Seed(VariableScope.Global, null, "global");
		Seed(VariableScope.Script, outer.Id.ToString(), "outer");

		await _runner.RunAsync(outer.Id, null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_markers, Is.EqualTo(new[] { "global" }));
			Assert.That(_capture.CapturedContext!.ScriptId, Is.EqualTo(inner.Id.ToString()));
		});
	}

	[Test]
	public async Task A_step_in_a_script_is_told_which_script_it_runs_in()
	{
		var script = await Store(ReadXFlow);

		await _runner.RunAsync(script.Id, null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_capture.CapturedContext!.ScriptId, Is.EqualTo(script.Id.ToString()));
			Assert.That(_capture.CapturedContext.AutomationId, Is.Null);
		});
	}

	[Test]
	public async Task An_automation_reads_its_own_local_and_its_steps_are_told_which_automation_runs()
	{
		var automationId = Guid.NewGuid().ToString();
		Seed(VariableScope.Global, null, "global");
		Seed(VariableScope.Automation, automationId, "automation");
		Seed(VariableScope.Automation, Guid.NewGuid().ToString(), "another automation");

		await _flowExecutor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = ScriptFlows.ToFlowsSource(ReadXFlow),
				Trigger = TriggerSelector.ByType(ScriptFlows.TriggerType),
				Scope = VariableScope.Automation,
				ScopeRefId = automationId,
				Origin = ExecutionOrigin.Host
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_markers, Is.EqualTo(new[] { "automation" }));
			Assert.That(_capture.CapturedContext!.AutomationId, Is.EqualTo(automationId));
			Assert.That(_capture.CapturedContext.ScriptId, Is.Null);
		});
	}

	private void Seed(VariableScope scope, string? scopeRefId, string value)
	{
		_variables.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = "x",
			Scope = scope,
			ScopeRefId = scopeRefId,
			Type = VariableType.Text,
			Classification = VariableClassification.User,
			Value = value
		});
	}

	private static string RunScriptFlow(Guid scriptId)
	{
		return $$"""
				 [
				   {
				     "triggerId": "t1", "triggerType": "onRun",
				     "children": [
				       { "id": "b1", "type": "action", "blockType": "scripts.run-script",
				         "integrationId": "scripts", "actionId": "run-script",
				         "parameters": [{ "name": "scriptId", "type": "string", "value": "{{scriptId}}" }] }
				     ]
				   }
				 ]
				 """;
	}

	private async Task<ScriptEntity> Store(string flows, bool runsOnWidget = false)
	{
		var script = new ScriptEntity
		{
			Id = Guid.NewGuid(),
			Name = "Script",
			Flows = flows,
			Inputs = [],
			RunsOnWidget = runsOnWidget,
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow
		};
		await _scripts.AddOrUpdate(script);
		return script;
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

	private sealed class DirectScriptApi(ScriptRunner runner, ScriptCache scripts) : IScriptApi
	{
		public IReadOnlyList<SdkScript> GetScripts()
		{
			return scripts.GetAll()
				.Select(script => new SdkScript { Id = script.Id.ToString(), Name = script.Name })
				.ToList();
		}

		public async Task<ActionResult> RunAsync(
			string scriptId,
			IReadOnlyDictionary<string, object?>? inputs = null,
			string? originClientId = null,
			string? ownerWidgetId = null,
			CancellationToken cancellationToken = default)
		{
			var result = await runner.RunAsync(Guid.Parse(scriptId),
				originClientId,
				cancellationToken,
				0,
				inputs,
				ownerWidgetId);
			return result.Status == FlowExecutionStatus.Succeeded
				? ActionResult.Success()
				: ActionResult.Failed(result.ErrorCode ?? ActionExecutionErrorCodes.FlowError, "The script failed.");
		}
	}
}
