using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;
using EngineOutcomeStatus = MacroDeckHost.Application.Actions.ActionOutcomeStatus;

namespace MacroDeckHost.Tests.UnitTests;

public class RunActionFlowRequestMessageHandlerTests
{
	private const string _flows = """
								  [
								    { "triggerId": "t1", "triggerType": "onShortPress", "children": [] },
								    { "triggerId": "event-a", "triggerType": "onEvent", "children": [] }
								  ]
								  """;

	private RecordingFlowExecutor _executor = null!;
	private RunActionFlowRequestMessageHandler _handler = null!;

	[SetUp]
	public void SetUp()
	{
		_executor = new RecordingFlowExecutor();
		_handler = new RunActionFlowRequestMessageHandler(_executor, Log.Logger);
	}

	// This handler calls IFlowExecutor directly and bypasses ActionExecutionCoordinator entirely, so
	// it must be exercised against the real FlowExecutor - a fake IFlowExecutor (like the
	// RecordingFlowExecutor used everywhere above) would hide a guard that was only ever added to the
	// coordinator.
	[Test]
	public async Task Locked_is_refused_with_HOST_LOCKED_and_no_action_runs()
	{
		var action = new CapturingActionDefinition();
		var registry = new FakeIntegrationRegistry();
		registry.Add(new FakeIntegration { Id = "integration", Actions = [action] });
		var lockState = new FakeHostLockState { IsLocked = true };
		var flowExecutor = new FlowExecutor(registry,
			new FakeVariableTemplateRenderer(),
			new PassthroughConditionEvaluator(),
			new FakeSecretService(),
			new NullActionInteractions(),
			new NullUiInteractions(),
			new UserNotificationStore(),
			new MusicPlayerPollNudge(registry),
			lockState,
			TestLocalization.Preferences,
			TestLocalization.Resolver,
			Log.Logger);
		var handler = new RunActionFlowRequestMessageHandler(flowExecutor, Log.Logger);

		const string flow = """
							[
							  { "triggerId": "t1", "triggerType": "onShortPress", "children": [
							    { "id": "b1", "type": "action", "blockType": "integration.capture",
							      "integrationId": "integration", "actionId": "capture", "parameters": [] } ] }
							]
							""";

		var response = await handler.Handle(new RunActionFlowRequest { Flows = flow, TriggerId = "t1" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error?.Code, Is.EqualTo(ActionExecutionErrorCodes.HostLocked));
			Assert.That(action.ExecuteCount, Is.EqualTo(0));
		});
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

	[Test]
	public async Task Runs_the_named_flow_in_the_requested_scope()
	{
		var response = await _handler.Handle(new RunActionFlowRequest
			{
				Flows = _flows,
				TriggerId = "t1",
				Scope = "actionButton",
				ScopeRefId = "widget-1",
				ClientId = "client-1"
			},
			CancellationToken.None);

		Assert.That(response.Success, Is.True);
		Assert.That(_executor.Request, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(_executor.Request!.Trigger.ByTriggerId, Is.True);
			Assert.That(_executor.Request.Trigger.Value, Is.EqualTo("t1"));
			Assert.That(_executor.Request.Scope, Is.EqualTo(VariableScope.Widget));
			Assert.That(_executor.Request.ScopeRefId, Is.EqualTo("widget-1"));
			Assert.That(_executor.Request.OriginClientId, Is.EqualTo("client-1"));
		});
	}

	[Test]
	public async Task Wraps_the_flow_array_in_the_envelope_the_executor_reads()
	{
		await _handler.Handle(new RunActionFlowRequest { Flows = _flows, TriggerId = "t1" },
			CancellationToken.None);

		Assert.That(WidgetFlowsJson.TryExtract(_executor.Request?.FlowsSource, out var extracted), Is.True);
		Assert.That(extracted, Is.EqualTo(_flows));
	}

	[Test]
	public async Task Defaults_to_the_global_scope()
	{
		await _handler.Handle(new RunActionFlowRequest { Flows = _flows, TriggerId = "t1" },
			CancellationToken.None);

		Assert.That(_executor.Request?.Scope, Is.EqualTo(VariableScope.Global));
	}

	[TestCase("", "t1", TestName = "Missing_flows_are_rejected")]
	[TestCase(_flows, "", TestName = "Missing_trigger_id_is_rejected")]
	public async Task Incomplete_requests_are_rejected(string flows, string triggerId)
	{
		var response = await _handler.Handle(new RunActionFlowRequest { Flows = flows, TriggerId = triggerId },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error?.Code, Is.EqualTo("VALIDATION_ERROR"));
			Assert.That(_executor.Request, Is.Null);
		});
	}

	[Test]
	public async Task Unknown_scope_is_rejected()
	{
		var response = await _handler.Handle(new RunActionFlowRequest
			{
				Flows = _flows,
				TriggerId = "t1",
				Scope = "somethingElse"
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error?.Code, Is.EqualTo("VALIDATION_ERROR"));
			Assert.That(_executor.Request, Is.Null);
		});
	}

	[Test]
	public async Task An_exception_outside_the_executors_contract_is_sanitized_not_thrown()
	{
		_executor.Throw = new InvalidOperationException("boom");

		var response = await _handler.Handle(new RunActionFlowRequest { Flows = _flows, TriggerId = "t1" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error?.Code, Is.EqualTo(ActionExecutionErrorCodes.ActionFailed));
			Assert.That(TestLocalization.Resolve(response.Error?.Message), Is.Not.EqualTo("boom"));
		});
	}

	[Test]
	public async Task A_run_that_outlives_the_timeout_is_reported_with_what_already_ran()
	{
		var handler = new RunActionFlowRequestMessageHandler(_executor, Log.Logger, TimeSpan.FromMilliseconds(50));
		_executor.WaitForTimeout = true;
		_executor.Result = new FlowExecutionResult
		{
			ExecutionId = Guid.NewGuid(),
			Status = FlowExecutionStatus.Cancelled,
			MatchedFlows = 1,
			Actions =
			[
				new ActionExecutionOutcome("b1",
					"First",
					"system",
					"run-command",
					EngineOutcomeStatus.Succeeded,
					null,
					null,
					5)
			]
		};

		var response = await handler.Handle(new RunActionFlowRequest { Flows = _flows, TriggerId = "t1" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error?.Code, Is.EqualTo("TIMEOUT"));
			Assert.That(response.Status, Is.EqualTo(ActionExecutionStatus.Cancelled));
			Assert.That(response.Actions, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public void A_caller_that_aborts_propagates_instead_of_reporting_a_timeout()
	{
		using var aborted = new CancellationTokenSource();
		var handler = new RunActionFlowRequestMessageHandler(_executor, Log.Logger, TimeSpan.FromMinutes(2));
		_executor.WaitForTimeout = true;
		aborted.Cancel();

		Assert.That(async () => await handler.Handle(new RunActionFlowRequest { Flows = _flows, TriggerId = "t1" },
				aborted.Token),
			Throws.InstanceOf<OperationCanceledException>());
	}

	[Test]
	public async Task Unknown_trigger_id_is_reported_as_flow_not_found()
	{
		_executor.Result = new FlowExecutionResult
		{
			ExecutionId = Guid.NewGuid(),
			Status = FlowExecutionStatus.Succeeded,
			MatchedFlows = 0
		};

		var response = await _handler.Handle(new RunActionFlowRequest { Flows = _flows, TriggerId = "does-not-exist" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error?.Code, Is.EqualTo("FLOW_NOT_FOUND"));
		});
	}

	[Test]
	public async Task A_partially_failing_flow_carries_the_per_action_list()
	{
		var outcomes = new List<ActionExecutionOutcome>
		{
			new("b1", "First", "integration", "ok", EngineOutcomeStatus.Succeeded, null, null, 5),
			new("b2", "Second", "integration", "bad", EngineOutcomeStatus.Failed, "PROVIDER_ERROR", "It broke.", 5)
		};
		_executor.Result = new FlowExecutionResult
		{
			ExecutionId = Guid.NewGuid(),
			Status = FlowExecutionStatus.PartiallyFailed,
			MatchedFlows = 1,
			Actions = outcomes,
			ErrorCode = "PROVIDER_ERROR",
			ErrorMessage = "It broke."
		};

		var response = await _handler.Handle(new RunActionFlowRequest { Flows = _flows, TriggerId = "t1" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Status, Is.EqualTo(ActionExecutionStatus.PartiallyFailed));
			Assert.That(response.Actions, Has.Count.EqualTo(2));
			Assert.That(response.Actions[1].Status,
				Is.EqualTo(Application.Ui.Transport.Messages.Actions.ActionOutcomeStatus.Failed));
			Assert.That(response.Actions[1].ErrorCode, Is.EqualTo("PROVIDER_ERROR"));
		});
	}

	private sealed class RecordingFlowExecutor : IFlowExecutor
	{
		public FlowExecutionRequest? Request { get; private set; }

		public Exception? Throw { get; set; }

		public FlowExecutionResult? Result { get; set; }

		public bool WaitForTimeout { get; set; }

		public async Task<FlowExecutionResult> ExecuteAsync(
			FlowExecutionRequest request,
			CancellationToken cancellationToken)
		{
			Request = request;
			if (Throw is not null)
			{
				throw Throw;
			}

			if (WaitForTimeout)
			{
				var completion = new TaskCompletionSource();
				await using (cancellationToken.Register(() => completion.TrySetResult()))
				{
					await completion.Task;
				}

				return Result ??
					new FlowExecutionResult
					{
						ExecutionId = request.ExecutionId,
						Status = FlowExecutionStatus.Cancelled,
						MatchedFlows = 1
					};
			}

			return Result ??
				new FlowExecutionResult
				{
					ExecutionId = request.ExecutionId,
					Status = FlowExecutionStatus.Succeeded,
					MatchedFlows = 1
				};
		}
	}
}
