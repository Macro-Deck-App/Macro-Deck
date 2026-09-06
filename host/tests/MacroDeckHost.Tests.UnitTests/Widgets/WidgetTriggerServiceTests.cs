using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Application.Widgets;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

public class WidgetTriggerServiceTests
{
	private static readonly Guid _widgetId = Guid.NewGuid();

	private static WidgetEntity Widget(string data = "{}")
		=> new() { Id = _widgetId, FolderId = Guid.NewGuid(), Type = WidgetTypeIds.ActionButton, Data = data };

	/// <summary>A state-mode button stepping through its states on a press, which is the default.</summary>
	private static WidgetEntity CyclingWidget()
		=> Widget("""{"stateMode":true,"states":[{"id":"a"},{"id":"b"}]}""");

	private static WidgetTriggerService CreateService(
		RecordingCoordinator coordinator,
		RecordingStateService stateService,
		out ServiceProvider services)
	{
		services = new ServiceCollection().AddSingleton<IActionButtonStateService>(stateService).BuildServiceProvider();

		return new WidgetTriggerService(services.GetRequiredService<IServiceScopeFactory>(),
			coordinator,
			Serilog.Log.Logger);
	}

	[Test]
	public async Task A_short_press_advances_state_before_the_flow_runs()
	{
		var order = new List<string>();
		var stateService = new RecordingStateService { OnAdvance = () => order.Add("advance") };
		var coordinator = new RecordingCoordinator { OnRun = _ => order.Add("flow") };
		var service = CreateService(coordinator, stateService, out var services);
		using var _ = services;

		await service.ExecuteAsync(CyclingWidget(),
			WidgetTriggerTypes.ShortPress,
			"client-1",
			originDeviceId: null,
			CancellationToken.None);

		Assert.That(order, Is.EqualTo(_advanceThenFlow));
	}

	private static readonly string[] _advanceThenFlow = ["advance", "flow"];

	[Test]
	public async Task A_short_press_on_a_button_that_turned_cycling_off_leaves_its_state_alone()
	{
		var stateService = new RecordingStateService();
		var coordinator = new RecordingCoordinator();
		var service = CreateService(coordinator, stateService, out var services);
		using var _ = services;

		await service.ExecuteAsync(
			Widget("""{"stateMode":true,"cycleStatesOnPress":false,"states":[{"id":"a"},{"id":"b"}]}"""),
			WidgetTriggerTypes.ShortPress,
			"client-1",
			originDeviceId: null,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(stateService.AdvanceCalls, Is.Zero);
			Assert.That(coordinator.Runs, Is.EqualTo(1), "the flow still runs");
		});
	}

	[Test]
	public async Task A_legacy_toggle_mode_button_still_cycles_on_a_short_press()
	{
		var stateService = new RecordingStateService();
		var coordinator = new RecordingCoordinator();
		var service = CreateService(coordinator, stateService, out var services);
		using var _ = services;

		await service.ExecuteAsync(Widget("""{"mode":"toggle"}"""),
			WidgetTriggerTypes.ShortPress,
			"client-1",
			originDeviceId: null,
			CancellationToken.None);

		Assert.That(stateService.AdvanceCalls, Is.EqualTo(1));
	}

	[Test]
	public async Task A_long_press_does_not_advance_state()
	{
		var stateService = new RecordingStateService();
		var coordinator = new RecordingCoordinator();
		var service = CreateService(coordinator, stateService, out var services);
		using var _ = services;

		await service.ExecuteAsync(CyclingWidget(),
			WidgetTriggerTypes.LongPress,
			"client-1",
			originDeviceId: null,
			CancellationToken.None);

		Assert.That(stateService.AdvanceCalls, Is.Zero);
	}

	[Test]
	public async Task A_state_advance_that_throws_still_lets_the_flow_run()
	{
		var stateService = new RecordingStateService { ThrowOnAdvance = true };
		var coordinator = new RecordingCoordinator();
		var service = CreateService(coordinator, stateService, out var services);
		using var _ = services;

		await service.ExecuteAsync(CyclingWidget(),
			WidgetTriggerTypes.ShortPress,
			"client-1",
			originDeviceId: null,
			CancellationToken.None);

		Assert.That(coordinator.Runs, Is.EqualTo(1));
	}

	[Test]
	public async Task The_state_advance_never_observes_the_callers_cancellation_token()
	{
		var stateService = new RecordingStateService();
		var coordinator = new RecordingCoordinator();
		var service = CreateService(coordinator, stateService, out var services);
		using var _ = services;

		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		// The caller's token is already cancelled; the advance must still run because it is deliberately
		// not linked to it.
		await service.ExecuteAsync(CyclingWidget(),
			WidgetTriggerTypes.ShortPress,
			"client-1",
			originDeviceId: null,
			cts.Token);

		Assert.That(stateService.AdvanceCalls, Is.EqualTo(1));
	}

	[Test]
	public async Task The_flow_request_carries_the_exact_shape_the_old_handler_sent()
	{
		var stateService = new RecordingStateService();
		var coordinator = new RecordingCoordinator();
		var service = CreateService(coordinator, stateService, out var services);
		using var _ = services;
		var widget = Widget("{\"stateMode\":true}");

		await service.ExecuteAsync(widget,
			WidgetTriggerTypes.ShortPress,
			"client-42",
			originDeviceId: null,
			CancellationToken.None);

		var request = coordinator.LastRequest!;
		Assert.Multiple(() =>
		{
			Assert.That(request.FlowsSource, Is.EqualTo(widget.Data));
			Assert.That(request.Trigger.Value, Is.EqualTo(WidgetTriggerTypes.ShortPress));
			Assert.That(request.Scope, Is.EqualTo(VariableScope.Widget));
			Assert.That(request.ScopeRefId, Is.EqualTo(widget.Id.ToString()));
			Assert.That(request.OwnerWidgetId, Is.EqualTo(widget.Id));
			Assert.That(request.OriginClientId, Is.EqualTo("client-42"));
		});
	}

	[Test]
	public async Task The_flow_execution_is_bounded_to_five_seconds()
	{
		var stateService = new RecordingStateService();
		var coordinator = new RecordingCoordinator();
		var service = CreateService(coordinator, stateService, out var services);
		using var _ = services;

		await service.ExecuteAsync(Widget(),
			WidgetTriggerTypes.ShortPress,
			"client-1",
			originDeviceId: null,
			CancellationToken.None);

		Assert.That(coordinator.LastBound, Is.EqualTo(TimeSpan.FromSeconds(5)));
	}

	private sealed class RecordingStateService : IActionButtonStateService
	{
		public int AdvanceCalls { get; private set; }

		public bool ThrowOnAdvance { get; set; }

		public Action? OnAdvance { get; set; }

		public Task<WidgetStateWriteResult> SetAsync(Guid widgetId,
			string stateId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));

		public Task<WidgetStateWriteResult> AdvanceAsync(Guid widgetId, CancellationToken cancellationToken = default)
		{
			AdvanceCalls++;
			OnAdvance?.Invoke();

			if (ThrowOnAdvance)
			{
				throw new InvalidOperationException("boom");
			}

			return Task.FromResult(WidgetStateWriteResult.Succeeded("b"));
		}
	}

	private sealed class RecordingCoordinator : IActionExecutionCoordinator
	{
		public int Runs { get; private set; }

		public FlowExecutionRequest? LastRequest { get; private set; }

		public TimeSpan? LastBound { get; private set; }

		public Action<FlowExecutionRequest>? OnRun { get; set; }

		public Task<ActionExecutionDispatch> RunBoundedAsync(
			FlowExecutionRequest request,
			TimeSpan bound,
			CancellationToken cancellationToken)
		{
			Runs++;
			LastRequest = request;
			LastBound = bound;
			OnRun?.Invoke(request);

			var result = new FlowExecutionResult
			{
				ExecutionId = request.ExecutionId,
				Status = FlowExecutionStatus.Succeeded,
				MatchedFlows = 1
			};

			return Task.FromResult(new ActionExecutionDispatch(request.ExecutionId, result));
		}
	}
}
