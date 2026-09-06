using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests;

public class ActionExecutionCoordinatorTests
{
	private static readonly TimeSpan _generousBound = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan _tinyBound = TimeSpan.FromMilliseconds(20);

	private GatedFlowExecutor _flow = null!;
	private FakeUiTransport _transport = null!;
	private ServiceProvider _services = null!;
	private ActionExecutionCoordinator _coordinator = null!;

	[SetUp]
	public void SetUp()
	{
		_flow = new GatedFlowExecutor();
		_transport = new FakeUiTransport();
		_services = new ServiceCollection().AddSingleton<IFlowExecutor>(_flow).BuildServiceProvider();
		_coordinator = new ActionExecutionCoordinator(_services.GetRequiredService<IServiceScopeFactory>(),
			_transport,
			new NeverStoppingLifetime(),
			Log.Logger);
	}

	[TearDown]
	public void TearDown()
	{
		_services.Dispose();
		_transport.Dispose();
	}

	[Test]
	public async Task A_fast_run_returns_its_result_inline_and_publishes_no_event()
	{
		var request = Request();

		var dispatch = await _coordinator.RunBoundedAsync(request, _generousBound, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(dispatch.Result, Is.Not.Null);
			Assert.That(dispatch.Result!.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
			Assert.That(_transport.Sent, Is.Empty);
		});
	}

	[Test]
	public async Task A_slow_run_answers_accepted_and_publishes_exactly_one_event_to_the_origin_clients_group()
	{
		var request = Request();
		_flow.AddGate(request.ExecutionId);

		var dispatch = await _coordinator.RunBoundedAsync(request, _tinyBound, CancellationToken.None);
		Assert.That(dispatch.Result, Is.Null, "the run has not been released yet, so it must still be Accepted");

		_flow.Release(request.ExecutionId, Succeeded(request.ExecutionId));
		await _transport.WaitForSendAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_transport.Sent, Has.Count.EqualTo(1));
			Assert.That(_transport.Sent[0].Group, Is.EqualTo(UiClientGroups.For("client-1")));
			Assert.That(_transport.Sent[0].Message, Is.InstanceOf<ActionExecutionStatusEvent>());
			var statusEvent = (ActionExecutionStatusEvent)_transport.Sent[0].Message;
			Assert.That(statusEvent.ExecutionId, Is.EqualTo(request.ExecutionId.ToString()));
		});
	}

	[Test]
	public async Task A_slow_run_with_no_origin_client_publishes_nothing()
	{
		var request = Request(originClientId: null);
		_flow.AddGate(request.ExecutionId);

		var dispatch = await _coordinator.RunBoundedAsync(request, _tinyBound, CancellationToken.None);
		Assert.That(dispatch.Result, Is.Null);

		_flow.Release(request.ExecutionId, Succeeded(request.ExecutionId));
		// There is no signal to wait on for "nothing happens" - a short grace window is the only way to
		// tell "not yet" from "never" without reintroducing a real multi-second wait.
		await Task.Delay(50);

		Assert.That(_transport.Sent, Is.Empty);
	}

	[Test]
	public async Task The_detached_run_survives_the_callers_token_being_cancelled()
	{
		using var callerCts = new CancellationTokenSource();
		var request = Request();
		_flow.AddGate(request.ExecutionId);

		var dispatch = await _coordinator.RunBoundedAsync(request, _tinyBound, callerCts.Token);
		Assert.That(dispatch.Result, Is.Null);

		callerCts.Cancel();

		_flow.Release(request.ExecutionId, Succeeded(request.ExecutionId));
		await _transport.WaitForSendAsync();

		Assert.That(_transport.Sent,
			Has.Count.EqualTo(1),
			"the caller's own cancellation must not have stopped the detached run from finishing and publishing");
	}

	[Test]
	public async Task Two_concurrent_slow_runs_publish_two_events_with_distinct_execution_ids()
	{
		var requestA = Request(originClientId: "client-a");
		var requestB = Request(originClientId: "client-b");
		_flow.AddGate(requestA.ExecutionId);
		_flow.AddGate(requestB.ExecutionId);

		var dispatchA = await _coordinator.RunBoundedAsync(requestA, _tinyBound, CancellationToken.None);
		var dispatchB = await _coordinator.RunBoundedAsync(requestB, _tinyBound, CancellationToken.None);
		Assert.Multiple(() =>
		{
			Assert.That(dispatchA.Result, Is.Null);
			Assert.That(dispatchB.Result, Is.Null);
			Assert.That(dispatchA.ExecutionId, Is.Not.EqualTo(dispatchB.ExecutionId));
		});

		_flow.Release(requestA.ExecutionId, Succeeded(requestA.ExecutionId));
		_flow.Release(requestB.ExecutionId, Succeeded(requestB.ExecutionId));
		await _transport.WaitForSendAsync(count: 2);

		Assert.Multiple(() =>
		{
			Assert.That(_transport.Sent, Has.Count.EqualTo(2));
			var groups = _transport.Sent.Select(s => s.Group).ToList();
			Assert.That(groups, Does.Contain(UiClientGroups.For("client-a")));
			Assert.That(groups, Does.Contain(UiClientGroups.For("client-b")));
			var executionIds = _transport.Sent.Select(s => ((ActionExecutionStatusEvent)s.Message).ExecutionId)
				.ToList();
			Assert.That(executionIds,
				Is.EquivalentTo(new[]
				{
					requestA.ExecutionId.ToString(), requestB.ExecutionId.ToString()
				}));
		});
	}

	[Test]
	public async Task A_run_that_faults_still_publishes_a_failed_result()
	{
		var request = Request();
		_flow.AddGate(request.ExecutionId);

		var dispatch = await _coordinator.RunBoundedAsync(request, _tinyBound, CancellationToken.None);
		Assert.That(dispatch.Result, Is.Null);

		_flow.Fault(request.ExecutionId, new InvalidOperationException("engine bug"));
		await _transport.WaitForSendAsync();

		var statusEvent = (ActionExecutionStatusEvent)_transport.Sent.Single().Message;
		Assert.That(statusEvent.Status, Is.EqualTo(ActionExecutionStatus.Failed));
	}

	[Test]
	public async Task Exceeding_the_in_flight_cap_answers_unavailable_instead_of_queueing()
	{
		var pending = new List<Task<ActionExecutionDispatch>>();
		for (var i = 0; i < 20; i++)
		{
			var request = Request();
			_flow.AddGate(request.ExecutionId);
			pending.Add(_coordinator.RunBoundedAsync(request, _generousBound, CancellationToken.None));
		}

		var overflow = Request();
		var overflowDispatch = await _coordinator.RunBoundedAsync(overflow, _tinyBound, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(overflowDispatch.Result, Is.Not.Null);
			Assert.That(overflowDispatch.Result!.Status, Is.EqualTo(FlowExecutionStatus.Failed));
			Assert.That(overflowDispatch.Result!.ErrorCode, Is.EqualTo(ActionExecutionErrorCodes.Unavailable));
		});

		// The 20 filler runs are deliberately left ungated/unawaited - they hold no thread and are
		// abandoned once the test completes.
		GC.KeepAlive(pending);
	}

	private static FlowExecutionRequest Request(string? originClientId = "client-1", Guid? executionId = null)
		=> new()
		{
			ExecutionId = executionId ?? Guid.NewGuid(),
			Trigger = TriggerSelector.ByType("onShortPress"),
			OriginClientId = originClientId
		};

	private static FlowExecutionResult Succeeded(Guid executionId) => new()
	{
		ExecutionId = executionId,
		Status = FlowExecutionStatus.Succeeded,
		MatchedFlows = 1
	};

	private sealed class GatedFlowExecutor : IFlowExecutor
	{
		private readonly Dictionary<Guid, TaskCompletionSource<FlowExecutionResult>> _gates = new();

		public void AddGate(Guid executionId)
		{
			lock (_gates)
			{
				_gates[executionId] = new TaskCompletionSource<FlowExecutionResult>(
					TaskCreationOptions.RunContinuationsAsynchronously);
			}
		}

		public void Release(Guid executionId, FlowExecutionResult result)
		{
			lock (_gates)
			{
				_gates[executionId].SetResult(result);
			}
		}

		public void Fault(Guid executionId, Exception exception)
		{
			lock (_gates)
			{
				_gates[executionId].SetException(exception);
			}
		}

		public Task<FlowExecutionResult> ExecuteAsync(FlowExecutionRequest request, CancellationToken cancellationToken)
		{
			TaskCompletionSource<FlowExecutionResult>? gate;
			lock (_gates)
			{
				_gates.TryGetValue(request.ExecutionId, out gate);
			}

			return gate?.Task ?? Task.FromResult(Succeeded(request.ExecutionId));
		}
	}

	private sealed class FakeUiTransport : IUiTransport, IDisposable
	{
		public Task SendToConnection<T>(string connectionId, T message, CancellationToken cancellationToken = default)
			where T : class
			=> Task.CompletedTask;

		public Task AddToGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task RemoveFromGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		private readonly SemaphoreSlim _signal = new(0);

		public void Dispose() => _signal.Dispose();

		public List<(string Group, object Message)> Sent { get; } = [];

		public Task Send<T>(T message, CancellationToken cancellationToken = default)
			where T : class
			=> Task.CompletedTask;

		public Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
			where T : class
		{
			lock (Sent)
			{
				Sent.Add((group, message));
			}

			_signal.Release();
			return Task.CompletedTask;
		}

		public async Task WaitForSendAsync(int count = 1, int timeoutMs = 2000)
		{
			for (var i = 0; i < count; i++)
			{
				var acquired = await _signal.WaitAsync(timeoutMs);
				if (!acquired)
				{
					Assert.Fail("Expected a SendToGroup call that never arrived.");
				}
			}
		}
	}

	private sealed class NeverStoppingLifetime : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted => CancellationToken.None;
		public CancellationToken ApplicationStopping => CancellationToken.None;
		public CancellationToken ApplicationStopped => CancellationToken.None;

		public void StopApplication()
		{
		}
	}
}
