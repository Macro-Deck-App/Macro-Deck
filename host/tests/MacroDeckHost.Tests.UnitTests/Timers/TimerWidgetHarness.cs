using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Timers;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Timers;

internal sealed class TimerWidgetHarness : IAsyncDisposable
{
	private readonly ServiceProvider _services;

	public TimerWidgetHarness()
	{
		Folders = new MutableFolderCache();
		Time = new ManualTimeProvider();
		Registry = new VariableRegistry();
		LockState = new FakeHostLockState();
		Triggers = new RecordingTriggerService(this);
		Prompt = new ScriptedDurationPrompt();
		FlowExecutor = new RecordingFlowExecutor(this);

		var services = new ServiceCollection();
		services.AddSingleton(Registry);
		services.AddSingleton<Mediator.IMediator, RecordingMediator>();
		services.AddSingleton<IUserVariableStore, NullUserVariableStore>();
		services.AddTestVariableService();
		services.AddScoped<IFlowExecutor>(_ => FlowExecutor);
		_services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

		var scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();
		Store = new TimerWidgetStore(Time);
		Variables = new TimerWidgetVariableWriter(Store, scopeFactory, Serilog.Core.Logger.None);
		Coordinator = new TimerWidgetCoordinator(Store,
			Variables,
			Triggers,
			Prompt,
			Folders,
			LockState,
			scopeFactory,
			Serilog.Core.Logger.None);
	}

	public MutableFolderCache Folders { get; }

	public ManualTimeProvider Time { get; }

	public VariableRegistry Registry { get; }

	public FakeHostLockState LockState { get; }

	public RecordingTriggerService Triggers { get; }

	public ScriptedDurationPrompt Prompt { get; }

	public RecordingFlowExecutor FlowExecutor { get; }

	public TimerWidgetStore Store { get; }

	public TimerWidgetVariableWriter Variables { get; }

	public TimerWidgetCoordinator Coordinator { get; }

	public async Task<WidgetEntity> AddCountdownAsync(int seconds = 10, bool ask = false)
	{
		var mode = ask ? "ask" : "fixed";
		var widget = Folders.Add(WidgetTypeIds.Countdown, $$"""{"mode":"{{mode}}","durationMinutes":{{seconds / 60}},"durationSeconds":{{seconds % 60}}}""");
		await Coordinator.SyncAsync(widget);

		return widget;
	}

	public async Task<WidgetEntity> AddStopwatchAsync()
	{
		var widget = Folders.Add(WidgetTypeIds.Stopwatch, "{}");
		await Coordinator.SyncAsync(widget);

		return widget;
	}

	public Task<bool> PressAsync(WidgetEntity widget, string? clientId = null, Guid? deviceId = null)
		=> Coordinator.HandleGestureAsync(widget.Id, TimerGesture.Press, clientId, deviceId);

	public Task<bool> HoldAsync(WidgetEntity widget, string? clientId = null)
		=> Coordinator.HandleGestureAsync(widget.Id, TimerGesture.LongPress, clientId, null);

	public async Task AdvanceAsync(TimeSpan delta)
	{
		Time.Now += delta;

		foreach (var transition in Store.Tick())
		{
			await Coordinator.ApplyTickAsync(transition);
		}
	}

	// A dialog answer is applied on the continuation of the awaited modal, which need not run inline.
	public static async Task EventuallyAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

		while (!condition() && DateTime.UtcNow < deadline)
		{
			await Task.Delay(10);
		}
	}

	public static Task QuietPeriodAsync() => Task.Delay(100);

	public string? Variable(WidgetEntity widget, string name)
		=> Registry.FindByName(VariableScope.Widget, widget.Id.ToString(), name)?.Value?.ToLowerInvariant();

	public TimerWidgetPhase? Phase(WidgetEntity widget) => Store.Get(widget.Id)?.Phase;

	public async ValueTask DisposeAsync() => await _services.DisposeAsync();

	internal sealed class RecordingTriggerService(TimerWidgetHarness harness) : IWidgetTriggerService
	{
		public List<TriggerCall> Calls { get; } = [];

		public Task<ActionExecutionDispatch> ExecuteAsync(WidgetEntity widget,
			string triggerType,
			string? originClientId,
			Guid? originDeviceId,
			CancellationToken cancellationToken)
		{
			var variables = harness.Registry.GetByScope(VariableScope.Widget, widget.Id.ToString())
				.ToDictionary(variable => variable.Name, variable => variable.Value?.ToLowerInvariant());
			lock (Calls)
			{
				Calls.Add(new TriggerCall(widget.Id, triggerType, originClientId, originDeviceId, variables));
			}

			return Task.FromResult(new ActionExecutionDispatch(Guid.NewGuid(), null));
		}
	}

	internal sealed record TriggerCall(
		Guid WidgetId,
		string Trigger,
		string? ClientId,
		Guid? DeviceId,
		IReadOnlyDictionary<string, string?> VariablesSeen);

	internal sealed class RecordingFlowExecutor(TimerWidgetHarness harness) : IFlowExecutor
	{
		public List<TriggerCall> Calls { get; } = [];

		public List<FlowExecutionRequest> Requests { get; } = [];

		public Task<FlowExecutionResult> ExecuteAsync(FlowExecutionRequest request, CancellationToken cancellationToken)
		{
			var widgetId = request.OwnerWidgetId ?? Guid.Empty;
			var variables = harness.Registry.GetByScope(VariableScope.Widget, widgetId.ToString())
				.ToDictionary(variable => variable.Name, variable => variable.Value?.ToLowerInvariant());
			lock (Calls)
			{
				Requests.Add(request);
				Calls.Add(new TriggerCall(widgetId, request.Trigger.Value, request.OriginClientId, request.OriginDeviceId,
					variables));
			}

			return Task.FromResult(new FlowExecutionResult
			{
				ExecutionId = request.ExecutionId, Status = FlowExecutionStatus.Succeeded,
			});
		}
	}

	internal sealed class ScriptedDurationPrompt : ICountdownDurationPrompt
	{
		public List<PromptCall> Calls { get; } = [];

		public Task<int?> AskAsync(Guid widgetId, string originClientId, int? initialSeconds,
			CancellationToken cancellationToken)
		{
			var answer = new TaskCompletionSource<int?>();
			cancellationToken.Register(() => answer.TrySetCanceled(cancellationToken));
			lock (Calls)
			{
				Calls.Add(new PromptCall(widgetId, originClientId, initialSeconds, answer, cancellationToken));
			}

			return answer.Task;
		}
	}

	internal sealed record PromptCall(
		Guid WidgetId,
		string ClientId,
		int? InitialSeconds,
		TaskCompletionSource<int?> Answer,
		CancellationToken Cancellation);

	private sealed class NullUserVariableStore : IUserVariableStore
	{
		public IReadOnlyList<VariableEntity> Load() => [];

		public void Save(IEnumerable<VariableEntity> userVariables)
		{
		}
	}
}

internal sealed class MutableFolderCache : IFolderCache
{
	private readonly List<FolderEntity> _folders = [new() { Name = "Home", Order = 0, Widgets = [] }];

	public WidgetEntity Add(string type, string data)
	{
		var widget = new WidgetEntity { Id = Guid.NewGuid(), FolderId = _folders[0].Id, Type = type, Data = data };
		_folders[0].Widgets.Add(widget);

		return widget;
	}

	public void Remove(WidgetEntity widget) => _folders[0].Widgets.Remove(widget);

	public List<FolderEntity> GetAllFolders() => [.. _folders];

	public Task InitializeCache() => Task.CompletedTask;

	public FolderEntity? GetFolderById(Guid id) => _folders.FirstOrDefault(folder => folder.Id == id);

	public List<FolderEntity> GetFoldersByParentId(Guid? parentId) => [.. _folders];

	public List<FolderEntity> GetFoldersByProfileId(Guid profileId) => [.. _folders];

	public Task AddOrUpdate(FolderEntity folder) => Task.CompletedTask;

	public Task AddOrUpdateRange(IReadOnlyCollection<FolderEntity> folders) => Task.CompletedTask;

	public Task<FolderSubtreeRemoval> RemoveSubtree(Guid rootId) => Task.FromResult(new FolderSubtreeRemoval(false, [], []));

	public void AddWidget(Guid folderId, WidgetEntity widget)
	{
	}

	public void AddWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
	{
	}

	public void UpdateWidget(Guid folderId, WidgetEntity widget)
	{
	}

	public void UpdateWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
	{
	}

	public void UpdateWidgetPositions(Guid folderId, IReadOnlyList<WidgetPlacement> placements)
	{
	}

	public void RemoveWidget(Guid folderId, Guid widgetId)
	{
	}

	public void RemoveWidgets(Guid folderId, IReadOnlyList<Guid> widgetIds)
	{
	}

	public void ReplaceWidgets(Guid folderId, IReadOnlyList<Guid> removeIds, IReadOnlyList<WidgetEntity> addWidgets)
	{
	}
}
