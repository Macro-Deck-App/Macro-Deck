using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests;

public class FlowExecutorTests
{
	private static readonly ILogger _logger = Log.Logger;
	private static readonly string[] _expectedTags = ["a", "b"];

	private const string _twoEventFlows = """
										  {
										    "flows": [
										      {
										        "triggerId": "event-a",
										        "triggerType": "onEvent",
										        "children": [
										          { "id": "b1", "type": "action", "blockType": "integration.capture",
										            "integrationId": "integration", "actionId": "capture",
										            "parameters": [{ "name": "marker", "type": "string", "value": "a" }] }
										        ]
										      },
										      {
										        "triggerId": "event-b",
										        "triggerType": "onEvent",
										        "children": [
										          { "id": "b2", "type": "action", "blockType": "integration.capture",
										            "integrationId": "integration", "actionId": "capture",
										            "parameters": [{ "name": "marker", "type": "string", "value": "b" }] }
										        ]
										      }
										    ]
										  }
										  """;

	private FakeSecretService _secretService = null!;
	private CapturingActionDefinition _action = null!;
	private CapturingActionDefinition _action2 = null!;
	private FakeIntegrationRegistry _registry = null!;
	private UserNotificationStore _notificationStore = null!;
	private FakeHostLockState _lockState = null!;
	private FlowExecutor _executor = null!;

	[SetUp]
	public void SetUp()
	{
		_secretService = new FakeSecretService();
		_action = new CapturingActionDefinition();
		_action2 = new CapturingActionDefinition { Id = "capture-2" };
		_notificationStore = new UserNotificationStore();
		_lockState = new FakeHostLockState();

		_registry = new FakeIntegrationRegistry();
		_registry.Add(new FakeIntegration
		{
			Id = "integration",
			Actions = [_action, _action2, new ThrowingActionDefinition()]
		});

		_executor = new FlowExecutor(_registry,
			new FakeVariableTemplateRenderer(),
			new PassthroughConditionEvaluator(),
			_secretService,
			new NullActionInteractions(),
			new NullUiInteractions(),
			_notificationStore,
			new MusicPlayerPollNudge(_registry),
			_lockState,
			TestLocalization.Preferences,
			TestLocalization.Resolver,
			_logger);
	}

	// ---- host lock gating ----------------------------------------------------------------------

	private const string _oneActionFlow = """
										  {
										    "flows": [
										      {
										        "triggerId": "t1",
										        "triggerType": "onShortPress",
										        "children": [
										          { "id": "b1", "type": "action", "blockType": "integration.capture",
										            "integrationId": "integration", "actionId": "capture", "parameters": [] }
										        ]
										      }
										    ]
										  }
										  """;

	private static FlowExecutionRequest OneActionRequest(ExecutionOrigin origin = ExecutionOrigin.Client) => new()
	{
		FlowsSource = _oneActionFlow,
		Trigger = TriggerSelector.ByType("onShortPress"),
		Scope = VariableScope.Widget,
		Origin = origin
	};

	[Test]
	public async Task Locked_client_origin_flow_is_refused_before_any_action_runs()
	{
		_lockState.IsLocked = true;

		var result = await _executor.ExecuteAsync(OneActionRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionExecutionErrorCodes.HostLocked));
			Assert.That(_action.ExecuteCount, Is.EqualTo(0));
		});
	}

	[Test]
	public async Task Locked_host_origin_flow_still_runs()
	{
		_lockState.IsLocked = true;

		var result = await _executor.ExecuteAsync(OneActionRequest(ExecutionOrigin.Host), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
			Assert.That(result.ErrorCode, Is.Null);
			Assert.That(_action.ExecuteCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Recovery_unlocking_lets_the_identical_request_succeed()
	{
		_lockState.IsLocked = true;
		var locked = await _executor.ExecuteAsync(OneActionRequest(), CancellationToken.None);

		_lockState.IsLocked = false;
		var unlocked = await _executor.ExecuteAsync(OneActionRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(locked.ErrorCode, Is.EqualTo(ActionExecutionErrorCodes.HostLocked));
			Assert.That(unlocked.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
			Assert.That(unlocked.ErrorCode, Is.Null);
			Assert.That(_action.ExecuteCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task An_inflight_flow_finishes_after_the_host_locks_mid_run()
	{
		var gate = new TaskCompletionSource();
		var blocking = new BlockingActionDefinition(gate.Task);
		_registry.Add(new FakeIntegration { Id = "blocking-integration", Actions = [blocking, _action2] });

		const string flows = """
							 {
							   "flows": [
							     {
							       "triggerId": "t1",
							       "triggerType": "onShortPress",
							       "children": [
							         { "id": "b1", "type": "action", "blockType": "integration.block",
							           "integrationId": "blocking-integration", "actionId": "block", "parameters": [] },
							         { "id": "b2", "type": "action", "blockType": "integration.capture",
							           "integrationId": "integration", "actionId": "capture-2", "parameters": [] }
							       ]
							     }
							   ]
							 }
							 """;

		var run = _executor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = flows,
				Trigger = TriggerSelector.ByType("onShortPress"),
				Scope = VariableScope.Widget
			},
			CancellationToken.None);

		await blocking.Started.Task;
		_lockState.IsLocked = true;
		gate.SetResult();

		var result = await run;

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
			Assert.That(result.ErrorCode, Is.Null);
			Assert.That(result.Actions, Has.Count.EqualTo(2));
			Assert.That(result.Actions,
				Has.All.Matches<ActionExecutionOutcome>(o => o.Status == ActionOutcomeStatus.Succeeded));
		});
	}

	private sealed class BlockingActionDefinition : IActionDefinition
	{
		private readonly Task _releaseWhen;

		public BlockingActionDefinition(Task releaseWhen) => _releaseWhen = releaseWhen;

		public TaskCompletionSource Started { get; } = new();

		public string Id => "block";
		public LocalizedText Name => "Block";
		public LocalizedText Description => "Blocks until released";
		public IReadOnlyList<ActionParameter> Parameters { get; init; } = [];
		public MacroDeckPlatform Platforms { get; init; } = MacroDeckPlatform.All;

		public IActionExecutor CreateExecutor() => new BlockingExecutor(this);

		private sealed class BlockingExecutor : IActionExecutor
		{
			private readonly BlockingActionDefinition _owner;

			public BlockingExecutor(BlockingActionDefinition owner) => _owner = owner;

			public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
			{
				_owner.Started.TrySetResult();
				await _owner._releaseWhen;
				return ActionResult.Success();
			}
		}
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

	private async Task<IReadOnlyDictionary<string, object>> Execute(string parametersJson)
	{
		var widgetData = $$"""
						   {
						     "flows": [
						       {
						         "triggerId": "t1",
						         "triggerType": "onShortPress",
						         "children": [
						           {
						             "id": "b1",
						             "type": "action",
						             "blockType": "integration.capture",
						             "integrationId": "integration",
						             "actionId": "capture",
						             "parameters": {{parametersJson}}
						           }
						         ]
						       }
						     ]
						   }
						   """;

		await _executor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = widgetData,
				Trigger = TriggerSelector.ByType("onShortPress"),
				Scope = VariableScope.Widget,
				ScopeRefId = "widget-1"
			},
			CancellationToken.None);

		Assert.That(_action.CapturedParameters, Is.Not.Null);
		return _action.CapturedParameters!;
	}

	[TestCase("11111111-1111-1111-1111-111111111111")]
	[TestCase(null)]
	public async Task Owning_widget_is_handed_to_the_action(string? widgetId)
	{
		const string widgetData = """
								  {
								    "flows": [
								      {
								        "triggerId": "t1",
								        "triggerType": "onShortPress",
								        "children": [
								          { "id": "b1", "type": "action", "blockType": "integration.capture",
								            "integrationId": "integration", "actionId": "capture", "parameters": [] }
								        ]
								      }
								    ]
								  }
								  """;

		await _executor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = widgetData,
				Trigger = TriggerSelector.ByType("onShortPress"),
				Scope = VariableScope.Widget,
				OwnerWidgetId = widgetId is null ? null : Guid.Parse(widgetId)
			},
			CancellationToken.None);

		Assert.That(_action.CapturedOwnerWidgetId, Is.EqualTo(widgetId));
	}

	[TestCase(ActionResultStatus.Succeeded, true, true)]
	[TestCase(ActionResultStatus.Accepted, true, true)]
	[TestCase(ActionResultStatus.Failed, true, false)]
	[TestCase(ActionResultStatus.Succeeded, false, false)]
	[TestCase(ActionResultStatus.Accepted, false, false)]
	public async Task OnlyNonFailedStateProviderResults_ApplyExpectedStateAndQueueWidgetEvaluation(
		ActionResultStatus status,
		bool includeExpectedState,
		bool expectedToApply)
	{
		const string widgetData = """
								  {
								    "flows": [
								      {
								        "triggerId": "t1",
								        "triggerType": "onShortPress",
								        "children": [
								          { "id": "b1", "type": "action", "blockType": "provider.toggle",
								            "integrationId": "provider", "actionId": "toggle", "parameters": [] }
								        ]
								      }
								    ]
								  }
								  """;
		var result = (status, includeExpectedState) switch
		{
			(ActionResultStatus.Succeeded, true) => ActionResult.Success("on"),
			(ActionResultStatus.Succeeded, false) => ActionResult.Success(),
			(ActionResultStatus.Accepted, true) => ActionResult.Accepted("Waiting", "on"),
			(ActionResultStatus.Accepted, false) => ActionResult.Accepted("Waiting"),
			_ => new ActionResult
			{
				Status = ActionResultStatus.Failed,
				ErrorCode = ActionErrorCodes.ProviderError,
				ErrorMessage = "Failed",
				ExpectedStateId = "on"
			}
		};
		var action = new FakeStateProviderAction
		{
			Id = "toggle", Result = result
		};
		_registry.Add(new FakeIntegration { Id = "provider", Actions = [action] });
		var optimisticStates = new WidgetOptimisticStateStore(TimeProvider.System);
		var evalQueue = new WidgetStateEvalChannel();
		var executor = new FlowExecutor(_registry,
			new FakeVariableTemplateRenderer(),
			new PassthroughConditionEvaluator(),
			_secretService,
			new NullActionInteractions(),
			new NullUiInteractions(),
			_notificationStore,
			new MusicPlayerPollNudge(_registry),
			_lockState,
			TestLocalization.Preferences,
			TestLocalization.Resolver,
			_logger,
			optimisticStates,
			evalQueue);
		var widgetId = Guid.NewGuid();

		await executor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = widgetData,
				Trigger = TriggerSelector.ByType("onShortPress"),
				Scope = VariableScope.Widget,
				OwnerWidgetId = widgetId
			},
			CancellationToken.None);

		var identity = new WidgetOptimisticStateIdentity(widgetId, "b1", "provider", "toggle");
		Assert.Multiple(() =>
		{
			Assert.That(optimisticStates.Get(identity)?.ExpectedStateId,
				expectedToApply ? Is.EqualTo("on") : Is.Null);
			Assert.That(evalQueue.Reader.TryRead(out var queuedWidgetId), Is.EqualTo(expectedToApply));
			if (expectedToApply)
			{
				Assert.That(queuedWidgetId, Is.EqualTo(widgetId));
			}
		});
	}

	[Test]
	public async Task Duplicate_flows_for_one_trigger_run_actions_only_once()
	{
		const string widgetData = """
								  {
								    "flows": [
								      {
								        "triggerId": "t1",
								        "triggerType": "onShortPress",
								        "children": [
								          { "id": "b1", "type": "action", "blockType": "integration.capture",
								            "integrationId": "integration", "actionId": "capture", "parameters": [] }
								        ]
								      },
								      {
								        "triggerId": "t1",
								        "triggerType": "onShortPress",
								        "children": [
								          { "id": "b2", "type": "action", "blockType": "integration.capture",
								            "integrationId": "integration", "actionId": "capture", "parameters": [] }
								        ]
								      }
								    ]
								  }
								  """;

		await _executor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = widgetData,
				Trigger = TriggerSelector.ByType("onShortPress"),
				Scope = VariableScope.Widget,
				ScopeRefId = "widget-1"
			},
			CancellationToken.None);

		Assert.That(_action.ExecuteCount, Is.EqualTo(1));
	}

	[Test]
	public async Task Disabled_block_is_skipped_and_the_flow_continues()
	{
		const string widgetData = """
								  {
								    "flows": [
								      {
								        "triggerId": "t1",
								        "triggerType": "onShortPress",
								        "children": [
								          { "id": "b1", "type": "action", "blockType": "integration.capture",
								            "integrationId": "integration", "actionId": "capture", "disabled": true,
								            "parameters": [{ "name": "marker", "type": "string", "value": "off" }] },
								          { "id": "b2", "type": "action", "blockType": "integration.capture",
								            "integrationId": "integration", "actionId": "capture",
								            "parameters": [{ "name": "marker", "type": "string", "value": "on" }] }
								        ]
								      }
								    ]
								  }
								  """;

		var result = await _executor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = widgetData,
				Trigger = TriggerSelector.ByType("onShortPress"),
				Scope = VariableScope.Widget,
				ScopeRefId = "widget-1"
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_action.ExecuteCount, Is.EqualTo(1));
			Assert.That(_action.CapturedParameters?["marker"], Is.EqualTo("on"));
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
			Assert.That(result.Actions, Has.Count.EqualTo(2));
			Assert.That(result.Actions[0].Status, Is.EqualTo(ActionOutcomeStatus.Skipped));
			Assert.That(result.Actions[1].Status, Is.EqualTo(ActionOutcomeStatus.Succeeded));
		});
	}

	[Test]
	public async Task Disabled_container_does_not_run_its_children()
	{
		const string widgetData = """
								  {
								    "flows": [
								      {
								        "triggerId": "t1",
								        "triggerType": "onShortPress",
								        "children": [
								          {
								            "id": "loop", "type": "loop", "blockType": "repeatLoop", "disabled": true,
								            "parameters": [{ "name": "count", "type": "number", "value": 3 }],
								            "children": [
								              { "id": "b1", "type": "action", "blockType": "integration.capture",
								                "integrationId": "integration", "actionId": "capture", "parameters": [] }
								            ]
								          }
								        ]
								      }
								    ]
								  }
								  """;

		var result = await _executor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = widgetData,
				Trigger = TriggerSelector.ByType("onShortPress"),
				Scope = VariableScope.Widget,
				ScopeRefId = "widget-1"
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_action.ExecuteCount, Is.Zero);
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
			Assert.That(result.Actions, Has.Count.EqualTo(1));
			Assert.That(result.Actions[0].Status, Is.EqualTo(ActionOutcomeStatus.Skipped));
		});
	}

	[Test]
	public async Task Disabled_block_inside_a_branch_is_skipped()
	{
		const string widgetData = """
								  {
								    "flows": [
								      {
								        "triggerId": "t1",
								        "triggerType": "onShortPress",
								        "children": [
								          {
								            "id": "if", "type": "condition", "blockType": "ifElse",
								            "branches": [
								              {
								                "id": "br-else", "kind": "else",
								                "children": [
								                  { "id": "b1", "type": "action", "blockType": "integration.capture",
								                    "integrationId": "integration", "actionId": "capture",
								                    "disabled": true, "parameters": [] }
								                ]
								              }
								            ]
								          }
								        ]
								      }
								    ]
								  }
								  """;

		var result = await _executor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = widgetData,
				Trigger = TriggerSelector.ByType("onShortPress"),
				Scope = VariableScope.Widget,
				ScopeRefId = "widget-1"
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_action.ExecuteCount, Is.Zero);
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
			Assert.That(result.Actions, Has.Count.EqualTo(1));
			Assert.That(result.Actions[0].Status, Is.EqualTo(ActionOutcomeStatus.Skipped));
		});
	}

	[Test]
	public async Task Selecting_an_event_flow_by_trigger_id_runs_only_that_flow()
	{
		await _executor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = _twoEventFlows,
				Trigger = TriggerSelector.ById("event-b"),
				Scope = VariableScope.Widget,
				ScopeRefId = "widget-1"
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_action.ExecuteCount, Is.EqualTo(1));
			Assert.That(_action.CapturedParameters?["marker"], Is.EqualTo("b"));
		});
	}

	[Test]
	public async Task Selecting_event_flows_by_type_is_not_possible()
	{
		await _executor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = _twoEventFlows,
				Trigger = TriggerSelector.ById("does-not-exist"),
				Scope = VariableScope.Widget,
				ScopeRefId = "widget-1"
			},
			CancellationToken.None);

		Assert.That(_action.ExecuteCount, Is.Zero);
	}

	[Test]
	public async Task Event_parameters_reach_an_action_parameter()
	{
		const string widgetData = """
								  {
								    "flows": [
								      {
								        "triggerId": "event-a",
								        "triggerType": "onEvent",
								        "children": [
								          { "id": "b1", "type": "action", "blockType": "integration.capture",
								            "integrationId": "integration", "actionId": "capture",
								            "parameters": [
								              { "name": "scene", "type": "dynamic-choice", "value": { "$event": "sceneName" } },
								              { "name": "note", "type": "string", "value": "now: {{ event.sceneName }}" }
								            ] }
								        ]
								      }
								    ]
								  }
								  """;

		await _executor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = widgetData,
				Trigger = TriggerSelector.ById("event-a"),
				Scope = VariableScope.Widget,
				ScopeRefId = "widget-1",
				EventParameters = new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["sceneName"] = "Starting Soon"
				}
			},
			CancellationToken.None);

		Assert.That(_action.CapturedParameters?["scene"], Is.EqualTo("Starting Soon"));
	}

	[Test]
	public async Task Multiselect_array_becomes_string_array()
	{
		var parameters = await Execute("""
									   [{ "name": "tags", "type": "multiselect", "value": ["a", "b"] }]
									   """);

		Assert.That(parameters["tags"], Is.EqualTo(_expectedTags));
	}

	[Test]
	public async Task Missing_multiselect_defaults_to_empty_array()
	{
		var parameters = await Execute("""
									   [{ "name": "tags", "type": "multiselect", "value": null }]
									   """);

		Assert.That(parameters["tags"], Is.EqualTo(Array.Empty<string>()));
	}

	[Test]
	public async Task Secret_reference_is_resolved_to_plaintext()
	{
		var id = _secretService.Store("super-secret");
		var parameters = await Execute($$"""
										 [{ "name": "apiKey", "type": "password", "value": { "$secret": "{{id}}" } }]
										 """);

		Assert.That(parameters["apiKey"], Is.EqualTo("super-secret"));
	}

	[Test]
	public async Task Hotkey_object_is_passed_structured()
	{
		var parameters = await Execute("""
									   [{ "name": "shortcut", "type": "hotkey",
									      "value": { "modifiers": ["Ctrl", "Shift"], "key": "K", "code": "KeyK" } }]
									   """);

		var hotkey = parameters["shortcut"] as IReadOnlyDictionary<string, object?>;
		Assert.That(hotkey, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(hotkey!["key"], Is.EqualTo("K"));
			Assert.That(hotkey["modifiers"], Is.EqualTo(new List<object?> { "Ctrl", "Shift" }));
		});
	}

	[Test]
	public async Task Duration_string_with_units_is_parsed_to_milliseconds()
	{
		var parameters = await Execute("""
									   [{ "name": "delay", "type": "duration", "value": "2s" }]
									   """);

		Assert.That(parameters["delay"], Is.EqualTo(2000d));
	}

	[Test]
	public async Task Duration_number_passes_through()
	{
		var parameters = await Execute("""
									   [{ "name": "delay", "type": "duration", "value": 750 }]
									   """);

		Assert.That(parameters["delay"], Is.EqualTo(750L));
	}

	[Test]
	public async Task String_parameter_renders_liquid_but_choice_does_not()
	{
		var parameters = await Execute("""
									   [
									     { "name": "text", "type": "string", "value": "{{ test }}" },
									     { "name": "mode", "type": "choice", "value": "{{ test }}" }
									   ]
									   """);

		Assert.Multiple(() =>
		{
			Assert.That(parameters["text"], Is.EqualTo("rendered"));
			Assert.That(parameters["mode"], Is.EqualTo("{{ test }}"));
		});
	}

	[Test]
	public async Task Keyvalue_object_becomes_string_dictionary()
	{
		var parameters = await Execute("""
									   [{ "name": "headers", "type": "keyvalue",
									      "value": { "Accept": "application/json", "X-Test": "{{ test }}" } }]
									   """);

		var headers = parameters["headers"] as Dictionary<string, string>;
		Assert.That(headers, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(headers!["Accept"], Is.EqualTo("application/json"));
			Assert.That(headers["X-Test"], Is.EqualTo("rendered"));
		});
	}

	[Test]
	public async Task Unknown_type_falls_back_to_string()
	{
		var parameters = await Execute("""
									   [{ "name": "legacy", "type": "select", "value": "abc" }]
									   """);

		Assert.That(parameters["legacy"], Is.EqualTo("abc"));
	}

	[Test]
	public async Task Failing_action_raises_one_error_notification_that_a_retry_replaces()
	{
		const string flowData = """
								{
								  "flows": [
								    {
								      "triggerId": "t1",
								      "triggerType": "onShortPress",
								      "children": [
								        { "id": "b1", "type": "action", "blockType": "integration.explode",
								          "integrationId": "integration", "actionId": "explode", "parameters": [] }
								      ]
								    }
								  ]
								}
								""";
		var request = new FlowExecutionRequest
		{
			FlowsSource = flowData,
			Trigger = TriggerSelector.ByType("onShortPress"),
			Scope = VariableScope.Widget,
			ScopeRefId = "widget-1"
		};

		await _executor.ExecuteAsync(request, CancellationToken.None);
		await _executor.ExecuteAsync(request, CancellationToken.None);

		var notifications = _notificationStore.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(1));
			Assert.That(notifications[0].Severity, Is.EqualTo(UserNotificationSeverity.Error));
		});
	}

	[Test]
	public async Task All_actions_succeeding_is_reported_as_Succeeded()
	{
		var result = await RunFlow(SingleAction("b1", "integration", "capture"));

		Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
	}

	[Test]
	public async Task A_failed_action_makes_the_whole_flow_Failed_with_its_code_and_message()
	{
		_action.Result = ActionResult.Failed("PROVIDER_ERROR", "It broke.");

		var result = await RunFlow(SingleAction("b1", "integration", "capture"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo("PROVIDER_ERROR"));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Is.EqualTo("It broke."));
		});
	}

	[Test]
	public async Task One_failed_and_one_succeeded_action_is_PartiallyFailed_with_both_outcomes_present()
	{
		_action.Result = ActionResult.Failed("PROVIDER_ERROR", "It broke.");

		var result = await RunFlow($$"""
									 {
									   "flows": [
									     {
									       "triggerId": "t1", "triggerType": "onShortPress",
									       "children": [
									         {{SingleActionBlock("b1", "integration", "capture")}},
									         {{SingleActionBlock("b2", "integration", "capture-2")}}
									       ]
									     }
									   ]
									 }
									 """);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.PartiallyFailed));
			Assert.That(result.Actions, Has.Count.EqualTo(2));
			Assert.That(result.Actions[0].Status, Is.EqualTo(ActionOutcomeStatus.Failed));
			Assert.That(result.Actions[1].Status, Is.EqualTo(ActionOutcomeStatus.Succeeded));
			Assert.That(result.ErrorCode, Is.EqualTo("PROVIDER_ERROR"), "the aggregate carries the first failure");
		});
	}

	[Test]
	public async Task An_accepted_action_does_not_downgrade_the_aggregate_below_Succeeded()
	{
		_action.Result = ActionResult.Accepted("Waiting for confirmation.");

		var result = await RunFlow(SingleAction("b1", "integration", "capture"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
			Assert.That(result.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Accepted));
		});
	}

	[Test]
	public async Task A_condition_matching_no_branch_contributes_no_outcome_and_stays_Succeeded()
	{
		var result = await RunFlow("""
								   {
								     "flows": [
								       {
								         "triggerId": "t1", "triggerType": "onShortPress",
								         "children": [
								           {
								             "id": "if", "type": "condition", "blockType": "ifElse",
								             "branches": [
								               {
								                 "id": "br-if", "kind": "if",
								                 "condition": { "left": "a", "operator": "==", "right": "b" },
								                 "children": [
								                   { "id": "b1", "type": "action", "blockType": "integration.capture",
								                     "integrationId": "integration", "actionId": "capture",
								                     "parameters": [] }
								                 ]
								               }
								             ]
								           }
								         ]
								       }
								     ]
								   }
								   """);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
			Assert.That(result.Actions, Is.Empty);
			Assert.That(_action.ExecuteCount, Is.Zero);
		});
	}

	[Test]
	public async Task Missing_integration_or_action_id_is_INVALID_BLOCK()
	{
		var result = await RunFlow("""
								   {
								     "flows": [
								       {
								         "triggerId": "t1", "triggerType": "onShortPress",
								         "children": [
								           { "id": "b1", "type": "action", "blockType": "manualAction",
								             "parameters": [] }
								         ]
								       }
								     ]
								   }
								   """);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionExecutionErrorCodes.InvalidBlock));
		});
	}

	[Test]
	public async Task Disabled_integration_is_INTEGRATION_DISABLED()
	{
		_registry.SetEnabled("integration", false);

		var result = await RunFlow(SingleAction("b1", "integration", "capture"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionExecutionErrorCodes.IntegrationDisabled));
		});
	}

	[Test]
	public async Task Unknown_integration_id_is_INTEGRATION_NOT_FOUND()
	{
		var result = await RunFlow(SingleAction("b1", "nope", "capture"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionExecutionErrorCodes.IntegrationNotFound));
		});
	}

	// Issue #789: profiles saved while Teams was built in still carry its action blocks. The run must
	// record the gone step and carry on through the rest of the profile, not abort it.
	[Test]
	public async Task A_profile_step_for_a_removed_integration_fails_without_stopping_the_rest()
	{
		var result = await RunFlow($$"""
									 {
									   "flows": [
									     {
									       "triggerId": "t1", "triggerType": "onShortPress",
									       "children": [
									         {{SingleActionBlock("teams", "app.macro-deck.teams", "toggle-microphone")}},
									         {{SingleActionBlock("b2", "integration", "capture")}}
									       ]
									     }
									   ]
									 }
									 """);

		var teamsStep = result.Actions.Single(action => action.BlockId == "teams");

		Assert.Multiple(() =>
		{
			Assert.That(teamsStep.Status, Is.EqualTo(ActionOutcomeStatus.Failed));
			Assert.That(teamsStep.ErrorCode, Is.EqualTo(ActionExecutionErrorCodes.IntegrationNotFound));
			Assert.That(_action.ExecuteCount, Is.EqualTo(1), "the step after the removed one must still run");
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.PartiallyFailed));
		});
	}

	[Test]
	public async Task Unknown_action_id_is_ACTION_NOT_FOUND()
	{
		var result = await RunFlow(SingleAction("b1", "integration", "nope"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionExecutionErrorCodes.ActionNotFound));
		});
	}

	[Test]
	public async Task An_unsupported_block_type_is_UNSUPPORTED_BLOCK()
	{
		var result = await RunFlow("""
								   {
								     "flows": [
								       {
								         "triggerId": "t1", "triggerType": "onShortPress",
								         "children": [
								           { "id": "b1", "type": "widget-thing", "blockType": "widget-thing",
								             "parameters": [] }
								         ]
								       }
								     ]
								   }
								   """);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionExecutionErrorCodes.UnsupportedBlock));
		});
	}

	[Test]
	public async Task A_throwing_executor_produces_a_sanitized_message_not_the_exceptions_own()
	{
		_action.Throw = new InvalidOperationException("connection string: user=admin;password=hunter2");

		var result = await RunFlow(SingleAction("b1", "integration", "capture"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Failed));
			var outcome = result.Actions.Single();
			Assert.That(outcome.Status, Is.EqualTo(ActionOutcomeStatus.Failed));
			Assert.That(TestLocalization.Resolve(outcome.ErrorMessage), Does.Not.Contain("hunter2"));
			Assert.That(TestLocalization.Resolve(outcome.ErrorMessage), Does.Not.Contain("connection string"));
			Assert.That(outcome.ErrorCode, Is.EqualTo(ActionExecutionErrorCodes.ActionFailed));
		});
	}

	[Test]
	public async Task Cancellation_mid_flow_reports_Cancelled_with_the_outcomes_collected_so_far_and_does_not_throw()
	{
		using var cts = new CancellationTokenSource();
		_action.OnExecuted = () =>
		{
			if (_action.ExecuteCount == 1)
			{
				cts.Cancel();
			}
		};

		var widgetData = $$"""
						   {
						     "flows": [
						       {
						         "triggerId": "t1", "triggerType": "onShortPress",
						         "children": [
						           {
						             "id": "loop", "type": "loop", "blockType": "repeatLoop",
						             "parameters": [{ "name": "count", "type": "number", "value": 3 }],
						             "children": [
						               {{SingleActionBlock("b1", "integration", "capture")}}
						             ]
						           }
						         ]
						       }
						     ]
						   }
						   """;

		FlowExecutionResult result = null!;
		Assert.DoesNotThrowAsync(async () =>
			result = await _executor.ExecuteAsync(new FlowExecutionRequest
				{
					FlowsSource = widgetData,
					Trigger = TriggerSelector.ByType("onShortPress"),
					Scope = VariableScope.Widget,
					ScopeRefId = "widget-1"
				},
				cts.Token));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Cancelled));
			Assert.That(result.Actions, Has.Count.EqualTo(1));
			Assert.That(_action.ExecuteCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Break_inside_a_loop_stops_the_loop_without_failing_it()
	{
		const string widgetData = """
								  {
								    "flows": [
								      {
								        "triggerId": "t1", "triggerType": "onShortPress",
								        "children": [
								          {
								            "id": "loop", "type": "loop", "blockType": "repeatLoop",
								            "parameters": [{ "name": "count", "type": "number", "value": 3 }],
								            "children": [
								              { "id": "b1", "type": "action", "blockType": "integration.capture",
								                "integrationId": "integration", "actionId": "capture",
								                "parameters": [] },
								              { "id": "brk", "type": "flow-control", "blockType": "break",
								                "parameters": [] }
								            ]
								          }
								        ]
								      }
								    ]
								  }
								  """;

		var result = await RunFlow(widgetData);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
			Assert.That(_action.ExecuteCount, Is.EqualTo(1), "the loop must stop after the first `break`");
		});
	}

	[Test]
	public async Task Break_outside_a_loop_is_UNSUPPORTED_BLOCK()
	{
		const string widgetData = """
								  {
								    "flows": [
								      {
								        "triggerId": "t1", "triggerType": "onShortPress",
								        "children": [
								          { "id": "brk", "type": "flow-control", "blockType": "break",
								            "parameters": [] }
								        ]
								      }
								    ]
								  }
								  """;

		var result = await RunFlow(widgetData);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionExecutionErrorCodes.UnsupportedBlock));
		});
	}

	[Test]
	public async Task Multiple_correlated_runs_get_distinct_execution_ids_and_isolated_outcomes()
	{
		var flowOne = $$"""
						{
						  "flows": [
						    { "triggerId": "t1", "triggerType": "onShortPress",
						      "children": [ {{SingleActionBlock("b1", "integration", "capture")}} ] }
						  ]
						}
						""";
		var flowTwo = $$"""
						{
						  "flows": [
						    { "triggerId": "t1", "triggerType": "onShortPress",
						      "children": [ {{SingleActionBlock("b2", "integration", "capture-2")}} ] }
						  ]
						}
						""";

		var resultOne = await RunFlow(flowOne);
		var resultTwo = await RunFlow(flowTwo);

		Assert.Multiple(() =>
		{
			Assert.That(resultOne.ExecutionId, Is.Not.EqualTo(resultTwo.ExecutionId));
			Assert.That(resultOne.Actions.Single().BlockId, Is.EqualTo("b1"));
			Assert.That(resultTwo.Actions.Single().BlockId, Is.EqualTo("b2"));
			Assert.That(resultOne.Actions.Select(a => a.BlockId), Does.Not.Contain("b2"));
			Assert.That(resultTwo.Actions.Select(a => a.BlockId), Does.Not.Contain("b1"));
		});
	}

	private static string SingleActionBlock(string blockId, string integrationId, string actionId)
		=> $$"""
			 { "id": "{{blockId}}", "type": "action", "blockType": "{{integrationId}}.{{actionId}}",
			   "integrationId": "{{integrationId}}", "actionId": "{{actionId}}", "parameters": [] }
			 """;

	private static string SingleAction(string blockId, string integrationId, string actionId)
		=> $$"""
			 {
			   "flows": [
			     {
			       "triggerId": "t1", "triggerType": "onShortPress",
			       "children": [ {{SingleActionBlock(blockId, integrationId, actionId)}} ]
			     }
			   ]
			 }
			 """;

	private Task<FlowExecutionResult> RunFlow(string widgetData)
		=> _executor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = widgetData,
				Trigger = TriggerSelector.ByType("onShortPress"),
				Scope = VariableScope.Widget,
				ScopeRefId = "widget-1"
			},
			CancellationToken.None);
}
