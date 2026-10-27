using System.Text.Json.Nodes;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Integrations.Keyboard.Actions;
using MacroDeckHost.Integrations.Keyboard.Models;
using MacroDeckHost.Integrations.Keyboard.Native;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Keyboard;

[TestFixture("keyboard-sequence")]
[TestFixture("keyboardSequence")]
public class RunSequenceTemplatingTests
{
	private const string Injection =
		"""x"},{"type":"keyCombo","modifiers":["Win"],"key":"R"},{"type":"text","text":"cmd""";

	private readonly string _parameterType;
	private RecordingSequenceExecutor _keyboard = null!;
	private FlowExecutor _executor = null!;

	public RunSequenceTemplatingTests(string parameterType)
	{
		_parameterType = parameterType;
	}

	[SetUp]
	public void SetUp()
	{
		_keyboard = new RecordingSequenceExecutor();
		var integrations = new FakeIntegrationRegistry();
		integrations.Add(new FakeIntegration
		{
			Id = "keyboard",
			Actions = [new RunSequenceActionDefinition(_keyboard)]
		});

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
			Log.Logger);
	}

	[TestCase(Injection, TestName = "{m}(injected key combo step)")]
	[TestCase("""x"}],"repeat":50,"steps":[{"type":"keyCombo","modifiers":["Win"],"key":"R"}]}""",
		TestName = "{m}(injected sequence object)")]
	[TestCase("""x" , "type": "keyCombo", "key": "R", "modifiers": ["Win"], "x": "y""",
		TestName = "{m}(injected properties)")]
	[TestCase("""a"]}]}{"steps":[""", TestName = "{m}(unbalanced brackets)")]
	[TestCase("""C:\Users\me\"quoted"\\server\share\""", TestName = "{m}(quotes and backslashes)")]
	[TestCase("line one\nline two\r\n\ttabbed\u0001\u001b[31m\0end", TestName = "{m}(control characters)")]
	[TestCase("Grüße 👋 漢字 \u2028\u2029 \ud83d\ude00 \u202eevil", TestName = "{m}(unicode)")]
	[TestCase("\\u0022},{\\u0022type\\u0022:\\u0022keyCombo\\u0022", TestName = "{m}(escaped quotes)")]
	[TestCase("{{ event.message }} {% for i in 1..3 %}x{% end %}", TestName = "{m}(liquid in the event)")]
	public async Task Hostile_event_text_is_typed_verbatim_and_adds_no_steps(string message)
	{
		var result = await Run(Sequence(Text("{{ event.message }}")), message);

		Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
		Assert.That(_keyboard.Sequences, Has.Count.EqualTo(1));
		var sequence = _keyboard.Sequences[0];
		Assert.Multiple(() =>
		{
			Assert.That(Describe(sequence), Is.EqualTo(new[] { "text:" + message }));
			Assert.That(sequence.Repeat, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Hostile_text_in_every_templated_field_cannot_change_the_step_structure()
	{
		var sequence = new JsonObject
		{
			["steps"] = new JsonArray(new JsonObject
				{
					["type"] = "keyCombo",
					["modifiers"] = new JsonArray("Ctrl"),
					["key"] = "{{ event.message }}"
				},
				Text("{{ event.message }}"),
				new JsonObject { ["type"] = "delay", ["milliseconds"] = 10 },
				Text("before {{ event.message }} after")),
			["repeat"] = 1
		};

		await Run(sequence, Injection);

		Assert.That(_keyboard.Sequences, Has.Count.EqualTo(1));
		Assert.That(Describe(_keyboard.Sequences[0]),
			Is.EqualTo(new[]
			{
				"keyCombo:Ctrl+" + Injection,
				"text:" + Injection,
				"delay:10",
				"text:before " + Injection + " after"
			}));
	}

	[Test]
	public async Task A_lone_surrogate_in_the_event_text_stays_inside_one_text_step()
	{
		var result = await Run(Sequence(Text("{{ event.message }}")), "a\ud800b");

		Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
		Assert.That(Describe(_keyboard.Sequences.Single()), Is.EqualTo(new[] { "text:a�b" }));
	}

	[Test]
	public async Task A_malformed_template_is_typed_as_written_and_the_other_steps_still_render()
	{
		await Run(Sequence(Text("{{ event.message "), Text("{{ event.message }}")), "world");

		Assert.That(Describe(_keyboard.Sequences.Single()),
			Is.EqualTo(new[] { "text:{{ event.message ", "text:world" }));
	}

	[Test]
	public async Task Templated_text_steps_render_variables_and_event_values()
	{
		await Run(Sequence(Text("Hello {{ event.message }}!"), Text("{{ event.message | string.upcase }}")),
			"world");

		Assert.That(Describe(_keyboard.Sequences.Single()), Is.EqualTo(new[] { "text:Hello world!", "text:WORLD" }));
	}

	[Test]
	public async Task A_stored_sequence_from_an_earlier_beta_runs_unchanged()
	{
		var stored = JsonNode.Parse("""
									{
									  "steps": [
									    { "type": "keyCombo", "modifiers": ["Ctrl", "Shift"], "key": "P", "repeat": 2, "repeatDelayMs": 15 },
									    { "type": "text", "text": "He said \"hi\" in C:\\Temp\\{x}\n" },
									    { "type": "delay", "milliseconds": 250 },
									    { "type": "keyDown", "modifiers": [], "key": "Shift" },
									    { "type": "keyUp", "modifiers": [], "key": "Shift" }
									  ],
									  "repeat": 3,
									  "repeatDelayMs": 40
									}
									""")!;

		var result = await Run(stored, "unused");

		Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
		var sequence = _keyboard.Sequences.Single();
		Assert.Multiple(() =>
		{
			Assert.That(Describe(sequence),
				Is.EqualTo(new[]
				{
					"keyCombo:Ctrl+Shift+P x2/15",
					"text:He said \"hi\" in C:\\Temp\\{x}\n",
					"delay:250",
					"keyDown:Shift",
					"keyUp:Shift"
				}));
			Assert.That(sequence.Repeat, Is.EqualTo(3));
			Assert.That(sequence.RepeatDelayMs, Is.EqualTo(40));
		});
	}

	private async Task<FlowExecutionResult> Run(JsonNode sequence, string message)
	{
		var flows = new JsonObject
		{
			["flows"] = new JsonArray(new JsonObject
			{
				["triggerId"] = "chat",
				["triggerType"] = "onEvent",
				["children"] = new JsonArray(new JsonObject
				{
					["id"] = "b1",
					["type"] = "action",
					["blockType"] = "keyboard.run-sequence",
					["integrationId"] = "keyboard",
					["actionId"] = "run-sequence",
					["parameters"] = new JsonArray(new JsonObject
					{
						["name"] = "sequence",
						["type"] = _parameterType,
						["value"] = sequence
					})
				})
			})
		};

		return await _executor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = flows.ToJsonString(),
				Trigger = TriggerSelector.ById("chat"),
				EventParameters = new Dictionary<string, object?> { ["message"] = message }
			},
			CancellationToken.None);
	}

	private static JsonObject Sequence(params JsonObject[] steps)
	{
		return new JsonObject { ["steps"] = new JsonArray(steps.Cast<JsonNode?>().ToArray()) };
	}

	private static JsonObject Text(string text)
	{
		return new JsonObject { ["type"] = "text", ["text"] = text };
	}

	private static string[] Describe(KeyboardSequence sequence)
	{
		return sequence.Steps
			.Select(step => step switch
			{
				TextStep text => "text:" + text.Text,
				KeyComboStep { Repeat: > 1 } combo =>
					$"keyCombo:{Keys(combo.Modifiers, combo.Key)} x{combo.Repeat}/{combo.RepeatDelayMs}",
				KeyComboStep combo => "keyCombo:" + Keys(combo.Modifiers, combo.Key),
				DelayStep delay => "delay:" + delay.Milliseconds,
				KeyDownStep down => "keyDown:" + Keys(down.Modifiers, down.Key),
				KeyUpStep up => "keyUp:" + Keys(up.Modifiers, up.Key),
				_ => step.GetType().Name
			})
			.ToArray();
	}

	private static string Keys(IReadOnlyList<string> modifiers, string key)
	{
		return string.Join('+', modifiers.Append(key));
	}

	private sealed class RecordingSequenceExecutor : IKeyboardSequenceExecutor
	{
		public List<KeyboardSequence> Sequences { get; } = [];

		public Task<KeyboardSessionUnavailableReason?> ExecuteAsync(
			KeyboardSequence sequence,
			KeyboardTarget target = default,
			CancellationToken cancellationToken = default)
		{
			Sequences.Add(sequence);
			return Task.FromResult<KeyboardSessionUnavailableReason?>(null);
		}
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
