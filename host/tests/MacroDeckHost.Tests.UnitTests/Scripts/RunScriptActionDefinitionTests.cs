using System.Text.Json;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Integrations.Scripts;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Scripts;
using Serilog;
using SdkScript = MacroDeck.Sdk.Scripts.Script;
using SdkScriptInput = MacroDeck.Sdk.Scripts.ScriptInput;
using SdkScriptInputType = MacroDeck.Sdk.Scripts.ScriptInputType;

namespace MacroDeckHost.Tests.UnitTests.Scripts;

[TestFixture]
public class RunScriptActionDefinitionTests
{
	private static readonly string[] _sceneOnly = ["scene"];

	private RecordingScriptApi _scripts = null!;
	private RunScriptActionDefinition _action = null!;

	[SetUp]
	public void SetUp()
	{
		_scripts = new RecordingScriptApi();
		_action = new RunScriptActionDefinition(() => _scripts);
	}

	[Test]
	public async Task Input_parameters_are_collected_and_forwarded_to_the_script()
	{
		_scripts.Seed(new SdkScript { Id = "s1", Name = "Alpha" });

		await ExecuteAsync(("input:scene", "Live"), ("input:volume", 70d));

		var run = _scripts.Runs.Single();
		Assert.Multiple(() =>
		{
			Assert.That(run.ScriptId, Is.EqualTo("s1"));
			Assert.That(run.Inputs["scene"], Is.EqualTo("Live"));
			Assert.That(run.Inputs["volume"], Is.EqualTo(70d));
		});
	}

	[Test]
	public async Task Only_input_prefixed_parameters_travel_as_inputs()
	{
		_scripts.Seed(new SdkScript { Id = "s1", Name = "Alpha" });

		await ExecuteAsync(("input:scene", "Live"));

		Assert.That(_scripts.Runs.Single().Inputs.Keys, Is.EqualTo(_sceneOnly));
	}

	[Test]
	public async Task A_template_valued_input_arrives_rendered()
	{
		_scripts.Seed(new SdkScript { Id = "s1", Name = "Alpha" });

		await RunThroughAFlow("""{ "name": "input:scene", "type": "string", "value": "{{ vars.next_scene }}" }""",
			("next_scene", VariableType.Text, "Intermission"));

		var value = _scripts.Runs.Single().Inputs["scene"];
		Assert.Multiple(() =>
		{
			Assert.That(value, Is.EqualTo("Intermission"));
			Assert.That(value!.ToString(), Does.Not.Contain("{{"));
		});
	}

	[Test]
	public async Task A_reference_valued_input_arrives_typed()
	{
		_scripts.Seed(new SdkScript { Id = "s1", Name = "Alpha" });

		await RunThroughAFlow("""
							  { "name": "input:volume", "type": "number", "value": { "$var": "master_volume" } },
							  { "name": "input:muted", "type": "boolean", "value": true }
							  """,
			("master_volume", VariableType.Numeric, "70"));

		var run = _scripts.Runs.Single();
		Assert.Multiple(() =>
		{
			Assert.That(run.Inputs["volume"], Is.EqualTo(70d));
			Assert.That(run.Inputs["muted"], Is.EqualTo(true));
		});
	}

	[Test]
	public async Task The_script_option_carries_its_declarations_as_metadata()
	{
		_scripts.Seed(new SdkScript
		{
			Id = "s1",
			Name = "Alpha",
			Inputs =
			[
				new SdkScriptInput
				{
					Name = "scene",
					Type = SdkScriptInputType.Text,
					Label = "Scene name",
					Required = true,
					DefaultValue = "Starting Soon"
				}
			]
		});

		var options = await _action.GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = "scriptId",
				CurrentParameters = new Dictionary<string, object?>(StringComparer.Ordinal)
			},
			CancellationToken.None);

		var json = options.Options.Single().Metadata!["scriptInputs"];
		using var document = JsonDocument.Parse(json);
		var declared = document.RootElement.EnumerateArray().Single();
		Assert.Multiple(() =>
		{
			Assert.That(declared.GetProperty("name").GetString(), Is.EqualTo("scene"));
			Assert.That(declared.GetProperty("type").GetString(), Is.EqualTo("text"));
			Assert.That(declared.GetProperty("label").GetString(), Is.EqualTo("Scene name"));
			Assert.That(declared.GetProperty("required").GetBoolean(), Is.True);
			Assert.That(declared.GetProperty("defaultValue").GetString(), Is.EqualTo("Starting Soon"));
		});
	}

	[Test]
	public async Task A_widget_enabled_script_reads_self_from_the_owner_widget_at_the_call_site()
	{
		_scripts.Seed(new SdkScript { Id = "s1", Name = "Alpha", RunsOnWidget = true });

		await ExecuteAsync([("widget", "$self")], ownerWidgetId: "w1");

		Assert.That(_scripts.Runs.Single().OwnerWidgetId, Is.EqualTo("w1"));
	}

	// Acceptance scenario 3/4: a widget-enabled run with no owner widget must fail before any block
	// executes, and the literal sentinel "$self" must never itself reach the runner as if it were a
	// resolved id. This executor cannot observe the failure (its fake `IScriptApi` always succeeds -
	// that contract is ScriptRunner's, covered in ScriptInputFlowTests); what it owns is not letting
	// "$self" leak through unresolved, which is what would make the run silently succeed as no-op.
	[Test]
	public async Task A_widget_enabled_script_with_no_widget_context_never_forwards_the_literal_self_sentinel()
	{
		_scripts.Seed(new SdkScript { Id = "s1", Name = "Alpha", RunsOnWidget = true });

		await ExecuteAsync([("widget", "$self")], ownerWidgetId: null);

		var forwarded = _scripts.Runs.Single().OwnerWidgetId;
		Assert.That(forwarded, Is.Not.EqualTo("$self"));
		Assert.That(forwarded, Is.Empty, "the shape ScriptRunner's widget-required validation rejects");
	}

	[Test]
	public async Task A_toggle_off_script_never_resolves_or_forwards_an_owner_widget()
	{
		_scripts.Seed(new SdkScript { Id = "s1", Name = "Alpha", RunsOnWidget = false });

		await ExecuteAsync([], ownerWidgetId: "w1");

		Assert.That(_scripts.Runs.Single().OwnerWidgetId, Is.Null);
	}

	[Test]
	public async Task A_widget_enabled_script_option_carries_the_runsOnWidget_flag_in_metadata()
	{
		_scripts.Seed(new SdkScript { Id = "s1", Name = "Alpha", RunsOnWidget = true });

		var options = await _action.GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = "scriptId",
				CurrentParameters = new Dictionary<string, object?>(StringComparer.Ordinal)
			},
			CancellationToken.None);

		Assert.That(options.Options.Single().Metadata!["runsOnWidget"], Is.EqualTo("true"));
	}

	[Test]
	public async Task A_script_without_declarations_or_the_toggle_carries_no_metadata()
	{
		_scripts.Seed(new SdkScript { Id = "s1", Name = "Alpha" });

		var options = await _action.GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = "scriptId",
				CurrentParameters = new Dictionary<string, object?>(StringComparer.Ordinal)
			},
			CancellationToken.None);

		Assert.That(options.Options.Single().Metadata, Is.Null);
	}

	private Task<ActionResult> ExecuteAsync(params (string Name, object Value)[] parameters)
		=> ExecuteAsync(parameters, ownerWidgetId: null);

	private Task<ActionResult> ExecuteAsync(
		(string Name, object Value)[] parameters,
		string? ownerWidgetId)
	{
		var values = new Dictionary<string, object>(StringComparer.Ordinal) { ["scriptId"] = "s1" };
		foreach (var (name, value) in parameters)
		{
			values[name] = value;
		}

		return _action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = values,
			OwnerWidgetId = ownerWidgetId,
			CancellationToken = CancellationToken.None
		});
	}

	// The action deliberately renders nothing of its own: FlowExecutor resolves every block parameter
	// before the executor sees it, so the input:* convention is only observable through a real flow.
	private async Task RunThroughAFlow(
		string parametersJson,
		params (string Name, VariableType Type, string Value)[] globals)
	{
		var variables = new VariableRegistry();
		foreach (var (name, type, value) in globals)
		{
			variables.Upsert(new VariableEntity
			{
				Id = Guid.NewGuid(),
				Name = name,
				Scope = VariableScope.Global,
				Type = type,
				Classification = VariableClassification.User,
				Value = value
			});
		}

		var integrations = new FakeIntegrationRegistry();
		integrations.Add(new FakeIntegration { Id = "scripts", Actions = [_action] });

		var renderer = new VariableTemplateRenderer(variables);
		var executor = new FlowExecutor(integrations,
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

		var flows = $$"""
					  {
					    "flows": [
					      {
					        "triggerId": "t1", "triggerType": "onShortPress",
					        "children": [
					          { "id": "b1", "type": "action", "blockType": "scripts.run-script",
					            "integrationId": "scripts", "actionId": "run-script",
					            "parameters": [
					              { "name": "scriptId", "type": "string", "value": "s1" },
					              {{parametersJson}}
					            ] }
					        ]
					      }
					    ]
					  }
					  """;

		var result = await executor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = flows,
				Trigger = TriggerSelector.ByType("onShortPress"),
				Scope = VariableScope.Global
			},
			CancellationToken.None);

		Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
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

	private sealed class RecordingScriptApi : IScriptApi
	{
		private readonly List<SdkScript> _seeded = [];

		public List<(string ScriptId, IReadOnlyDictionary<string, object?> Inputs, string? OwnerWidgetId)> Runs { get; }
			= [];

		public void Seed(SdkScript script) => _seeded.Add(script);

		public IReadOnlyList<SdkScript> GetScripts() => _seeded;

		public Task<ActionResult> RunAsync(
			string scriptId,
			IReadOnlyDictionary<string, object?>? inputs = null,
			string? originClientId = null,
			string? ownerWidgetId = null,
			CancellationToken cancellationToken = default)
		{
			Runs.Add((scriptId,
				inputs ?? new Dictionary<string, object?>(StringComparer.Ordinal),
				ownerWidgetId));
			return ActionResult.SucceededTask;
		}
	}
}
