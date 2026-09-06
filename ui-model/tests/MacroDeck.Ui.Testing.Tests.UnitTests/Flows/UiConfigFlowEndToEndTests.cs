using System.Reflection;
using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Config.Validation;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Testing.Tests.UnitTests.Flows;

/// <summary>
/// Acceptance criterion 1 for issue #540, as acceptance scenario 33: a realistic multi-step configuration flow -
/// validation, a conditional section, an asynchronously loaded option list and a step transition - runs end to
/// end with only <c>MacroDeck.Ui</c>, <c>MacroDeck.Ui.Testing</c> and <c>MacroDeck.Ui.Model</c> loaded. If it
/// cannot, a plugin's UI is untestable without a host and a browser and the headless host is pointless.
///
/// <para>
/// Two things stop this from being a liveness test that a flow rendering everything at once would also pass.
/// The three negative checkpoints - a rejected submit, a removal expressed as <c>remove-node</c> rather than a
/// <c>replace-node</c> on the step, and a step that is genuinely absent until it is reached - and the fold at
/// the end, which replays every patch the flow produced onto the tree the flow started from and demands the
/// canonical bytes match.
/// </para>
/// </summary>
[TestFixture]
public class UiConfigFlowEndToEndTests
{
	/// <summary>The message the flow's own validation rule carries. The test owns it, so the rendered
	/// <c>validation-message</c> node is checked against what the rule was authored with rather than against
	/// whatever the runtime happened to compose.</summary>
	private const string _apiKeyRequiredMessage = "An API key is required.";

	private const string _basicMode = "basic";
	private const string _advancedMode = "advanced";
	private const string _credentialsStepId = "credentials";
	private const string _verifyStepId = "verify";

	private static readonly string[] _expectedModeValues = [_basicMode, _advancedMode];

	/// <summary>The <c>MacroDeck.*</c> assemblies the DSL is allowed to reference. Localization joined the
	/// model when text properties became localizable (#326); like the model, it references nothing itself,
	/// so it adds no transitive dependency to a plugin author's project.</summary>
	private static readonly string[] _expectedDslReferences = ["MacroDeck.Localization", "MacroDeck.Ui.Model"];

	/// <summary>The only <c>MacroDeck.*</c> assemblies the test host is allowed to reference: the DSL, plus the
	/// model it reaches transitively.</summary>
	private static readonly string[] _expectedTestingReferences = ["MacroDeck.Ui", "MacroDeck.Ui.Model"];

	[Test]
	public async Task
		A_multi_step_flow_with_validation_conditional_sections_and_async_loading_runs_end_to_end_in_the_test_host()
	{
		var flow = new SampleConfigFlow();

		// 1. Render. The option list is already loading, so the busy node is in the first tree and the step the
		//    flow has not reached yet is not.
		var host = flow.Render();
		var initialTree = host.Tree;

		// Nothing in this script is a defect, so nothing in it may be reported as one - the refused submit in step 3
		// least of all.
		var faults = new List<UiHandlerFaultEventArgs>();
		host.View.HandlerFaulted += (_, args) => faults.Add(args);

		Assert.Multiple(() =>
		{
			Assert.That(host.FindById("setup.credentials.busy"),
				Is.Not.Null,
				"a load in flight has to be visible as a node, not merely as a flag");
			Assert.That(host.SingleByType(UiConfigPrimitives.Busy).Id, Is.EqualTo("setup.credentials.busy"));
			Assert.That(host.FindById("setup." + _verifyStepId), Is.Null);
			Assert.That(OptionValues(host.ById("mode")), Is.Empty, "nothing has loaded yet");
			Assert.That(host.ById("mode").Flag(UiConfigProperties.Loading),
				Is.True,
				"the loading key is present exactly while a load is running");
		});

		// 2. The test completes the load, so "loading finished" is an event the test causes rather than a delay it
		//    waits out.
		flow.CompleteOptionLoad(UiOption.Of(_basicMode, "Basic"), UiOption.Of(_advancedMode, "Advanced"));
		await host.SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(host.FindById("setup.credentials.busy"), Is.Null, "the busy node has to be gone");
			Assert.That(host.ByType(UiConfigPrimitives.Busy), Is.Empty);
			Assert.That(OptionValues(host.ById("mode")),
				Is.EqualTo(_expectedModeValues).AsCollection,
				"the options have to arrive in source order");
		});

		// 3. First negative checkpoint: submitting an incomplete step is refused, and the refusal is visible as a
		//    validation-message node rather than only as a returned reason.
		var rejected = host.ById("setup." + _credentialsStepId).Submit();

		Assert.Multiple(() =>
		{
			Assert.That(rejected.IsAccepted, Is.False);

			// Verbatim, not wrapped: the handler declined, so the client is told what the handler said rather than
			// what a fault report made of it.
			Assert.That(rejected.Reason, Is.EqualTo(_apiKeyRequiredMessage));
			Assert.That(faults, Is.Empty, "a refused submit is an answer, not a defect");

			var messages = host.ByType(UiConfigPrimitives.ValidationMessage);

			Assert.That(messages, Has.Count.EqualTo(1));
			Assert.That(messages[0].Text(UiConfigProperties.Text), Is.EqualTo(_apiKeyRequiredMessage));
			Assert.That(messages[0].Text(UiConfigProperties.For), Is.EqualTo("apiKey"));
			Assert.That(host.FindById("setup." + _credentialsStepId),
				Is.Not.Null,
				"a refused submit leaves the flow on the step it refused");
			Assert.That(host.FindById("setup." + _verifyStepId), Is.Null);
		});

		// 4. Second negative checkpoint: filling the field removes the message node by naming it, not by replacing
		//    the step - a replace would throw away the focus and in-flight edits of every sibling field.
		var messageId = host.SingleByType(UiConfigPrimitives.ValidationMessage).Id;

		// No ClearPatches anywhere in the script: step 7 folds the whole stream from the very first tree, and a
		// cleared patch is one the fold would silently skip over.
		Assert.That(host.ById("apiKey").Change("k-1").IsAccepted, Is.True);

		await host.SettleAsync();

		var removalPatch = host.LastPatch;
		var removals = removalPatch.Operations
			.Where(operation => string.Equals(operation.Op, UiPatchOperations.RemoveNode, StringComparison.Ordinal))
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(host.ByType(UiConfigPrimitives.ValidationMessage), Is.Empty);
			Assert.That(removals, Has.Count.EqualTo(1));
			Assert.That(removals[0].NodeId, Is.EqualTo(messageId));
			Assert.That(removalPatch.Operations.Select(operation => operation.Op),
				Has.None.EqualTo(UiPatchOperations.ReplaceNode));
			Assert.That(host.ById("apiKey").Text(UiConfigProperties.Value), Is.EqualTo("k-1"));
		});

		// 5. The conditional section: one insert naming the section, at the position it renders at.
		Assert.That(host.ById("mode").Change(_advancedMode).IsAccepted, Is.True);

		await host.SettleAsync();

		var inserts = host.LastPatch.Operations
			.Where(operation => string.Equals(operation.Op, UiPatchOperations.InsertNode, StringComparison.Ordinal))
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(inserts, Has.Count.EqualTo(1));
			Assert.That(inserts[0].NodeId, Is.EqualTo("setup.credentials.advanced"));
			Assert.That(host.FindById("timeout"),
				Is.Not.Null,
				"an input inside chrome keeps its bare key, whatever the chrome is");
		});

		// 6. Third negative checkpoint: the step the flow left is genuinely gone, not merely hidden - a completed
		//    step that stayed in the tree would still submit its fields.
		Assert.That(host.ById("setup." + _credentialsStepId).Submit().IsAccepted, Is.True);

		await host.SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(host.FindById("setup." + _verifyStepId), Is.Not.Null);
			Assert.That(host.FindById("setup." + _credentialsStepId), Is.Null);
			Assert.That(host.FindById("apiKey"), Is.Null);
			Assert.That(host.ById("setup").Text(UiConfigProperties.StepId), Is.EqualTo(_verifyStepId));
		});

		// 7. What makes the whole script a correctness claim rather than a liveness one: every patch the flow
		//    produced, folded onto the tree it started from, is the tree it ended at - byte for byte.
		var folded = initialTree;

		foreach (var patch in host.Patches)
		{
			var result = UiTreeApplier.Apply(folded, patch);

			Assert.That(result.IsApplied, Is.True, result.RejectionReason);

			folded = result.Tree;
		}

		Assert.Multiple(() =>
		{
			Assert.That(UiCanonicalJson.Serialize(folded), Is.EqualTo(host.ToCanonicalJson()));
			Assert.That(folded.Revision, Is.EqualTo(host.Revision));
			Assert.That(faults, Is.Empty, "the whole script has to run without one handler being reported as broken");
		});

		// 8. "No host and no renderer", mechanically.
		AssertNoHostAndNoRendererIsLoaded();
	}

	/// <summary>The claim the whole criterion rests on, as an assertion rather than an assumption: the flow above
	/// ran with the DSL, the test host and the model, and nothing else was even loaded to run it.</summary>
	private static void AssertNoHostAndNoRendererIsLoaded()
	{
		var loaded = AppDomain.CurrentDomain.GetAssemblies()
			.Select(assembly => assembly.GetName().Name ?? string.Empty)
			.ToList();

		var offending = loaded
			.Where(name => name.StartsWith("MacroDeckHost", StringComparison.Ordinal) ||
				name.Contains("Renderer", StringComparison.OrdinalIgnoreCase))
			.ToList();

		var dslReferences = MacroDeckReferencesOf(typeof(UiView).Assembly);
		var testingReferences = MacroDeckReferencesOf(typeof(UiTestHost).Assembly);

		Assert.Multiple(() =>
		{
			Assert.That(offending, Is.Empty, "no host and no renderer assembly may be loaded to run a flow");
			Assert.That(dslReferences, Is.EqualTo(_expectedDslReferences).AsCollection);
			Assert.That(testingReferences, Is.EqualTo(_expectedTestingReferences).AsCollection);
		});
	}

	/// <summary>The <c>MacroDeck.*</c> assemblies <paramref name="assembly" /> references, sorted so the
	/// expectation is a set rather than a link order.</summary>
	private static List<string> MacroDeckReferencesOf(Assembly assembly)
		=> assembly.GetReferencedAssemblies()
			.Select(reference => reference.Name ?? string.Empty)
			.Where(name => name.StartsWith("MacroDeck", StringComparison.Ordinal))
			.OrderBy(name => name, StringComparer.Ordinal)
			.ToList();

	private static List<string> OptionValues(UiTestNode node)
	{
		var options = node.Property(UiConfigProperties.Options);

		if (options is not { ValueKind: JsonValueKind.Array } array)
		{
			return [];
		}

		var values = new List<string>();

		foreach (var option in array.EnumerateArray())
		{
			values.Add(option.GetProperty("value").GetString() ?? string.Empty);
		}

		return values;
	}

	/// <summary>
	/// The flow under test: a two-step setup whose first step carries an API key with a validation rule, a mode
	/// picked from an asynchronously loaded option list, and an advanced section gated on that mode. Only the
	/// active step is a child of the flow, so leaving a step removes it.
	/// </summary>
	private sealed class SampleConfigFlow
	{
		private readonly TaskCompletionSource<UiOptionResult> _optionLoad = new();
		private readonly UiState<string> _apiKey = new(string.Empty);
		private readonly UiState<string> _mode = new(_basicMode);
		private readonly UiState<string> _stepId = new(_credentialsStepId);
		private readonly UiState<bool> _submitAttempted = new(false);
		private readonly UiOptionsState _options;

		internal SampleConfigFlow()
			=> _options = new UiOptionsState(UiOptionSource.From((_, _) => _optionLoad.Task));

		/// <summary>Starts the option load and renders, so the first tree the test sees is one whose list is still
		/// loading.</summary>
		internal UiTestHost Render()
		{
			_options.Reload();

			return UiTestHost.Render(Build());
		}

		/// <summary>Lets the load the render started finish, with the options in the order a renderer must keep.
		/// </summary>
		internal void CompleteOptionLoad(params UiOption[] options)
			=> _optionLoad.SetResult(UiOptionResult.From(options));

		private UiFlow Build()
			=> new()
			{
				Key = "setup",
				Title = "Connect your account",
				StepId = UiValue.From(() => _stepId.Value),
				Children =
				[
					new UiWhen
					{
						Key = "credentialsGate",
						Condition = () => string.Equals(_stepId.Value, _credentialsStepId, StringComparison.Ordinal),
						Content = BuildCredentialsStep,
					},
					new UiWhen
					{
						Key = "verifyGate",
						Condition = () => string.Equals(_stepId.Value, _verifyStepId, StringComparison.Ordinal),
						Content = () => new UiStep { Key = _verifyStepId },
					},
				],
			};

		private UiStep BuildCredentialsStep()
			=> new()
			{
				Key = _credentialsStepId,
				Events = [UiEventHandler.On(UiConfigEvents.Submit, Submit)],
				Children =
				[
					new UiWhen
					{
						Key = "loadingGate",
						Condition = () => _options.IsLoading,
						Content = () => new UiBusy { Key = "busy", Text = "Loading modes..." },
					},
					new UiStringInput
					{
						Key = "apiKey",
						Label = "API key",
						Binding = Bind.To(_apiKey),
						ValidationRules =
						[
							UiValidationRule.Require(_apiKeyRequiredMessage, () => _apiKey.Value.Length > 0),
						],
					},
					new UiWhen
					{
						Key = "apiKeyMessageGate",
						Condition = () => _submitAttempted.Value && _apiKey.Value.Length == 0,
						Content = () => new UiValidationMessage
						{
							Key = "apiKeyMessage",
							Text = _apiKeyRequiredMessage,
							For = "apiKey",
						},
					},
					new UiDynamicChoiceInput
					{
						Key = "mode",
						Label = "Mode",
						Binding = Bind.To(_mode),
						OptionsState = _options,
					},
					new UiWhen
					{
						Key = "advancedGate",
						Condition = () => string.Equals(_mode.Value, _advancedMode, StringComparison.Ordinal),
						Content = () => new UiAdvancedSection
						{
							Key = "advanced",
							Label = "Advanced configuration",
							Children = [new UiDurationInput { Key = "timeout", Label = "Timeout" }],
						},
					},
				],
			};

		/// <summary>
		/// What the step's continue affordance does. Refusing is expressed by declining the event, never by
		/// throwing: an unfilled field is an ordinary answer the user reads, not a broken plugin a host logs.
		/// Whatever the handler wrote before declining is still flushed, so the validation message appears with the
		/// refusal.
		/// </summary>
		private UiEventOutcome Submit(UiEventData data)
		{
			if (_apiKey.Peek().Length == 0)
			{
				_submitAttempted.Value = true;

				return UiEventOutcome.Rejected(_apiKeyRequiredMessage);
			}

			_submitAttempted.Value = false;
			_stepId.Value = _verifyStepId;

			return UiEventOutcome.Accepted;
		}
	}
}
