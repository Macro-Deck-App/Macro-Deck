using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Rendering;

/// <summary>
/// Scenarios E1-E3: the widget-scoped vars.state/vars.state_label are read-only exactly like
/// vars.toggled was, they exist only while State Mode is on, and a set change (not a plain transition)
/// is what carries the new state list on the wire.
/// </summary>
[TestFixture]
public class WidgetStateVariablesAndPushTests
{
	[Test]
	public async Task StateModeEnabled_ExposesReadOnlyVarsStateAndVarsStateLabel_AndNoVarsToggled()
	{
		var widgetId = Guid.NewGuid();
		await using var harness = new VariableServiceHarness();

		await harness.Service.UpsertWidgetVariable(VariableScope.Widget,
			widgetId.ToString(),
			WidgetStateReconciler.StateVariableName,
			VariableType.Text,
			"on");
		await harness.Service.UpsertWidgetVariable(VariableScope.Widget,
			widgetId.ToString(),
			WidgetStateReconciler.StateLabelVariableName,
			VariableType.Text,
			"On");

		var scoped = await harness.Service.GetByScope(VariableScope.Widget, widgetId.ToString());
		var stateVar = scoped.SingleOrDefault(v => v.Name == "state");
		var stateLabelVar = scoped.SingleOrDefault(v => v.Name == "state_label");
		Assert.That(stateVar, Is.Not.Null, "vars.state must exist once the reconciler seeds it");
		Assert.That(stateLabelVar, Is.Not.Null, "vars.state_label must exist once the reconciler seeds it");

		var updateResult = await harness.Service.SetValue(stateVar!.Id, "off");
		var deleteResult = await harness.Service.DeleteUserVariable(stateLabelVar!.Id);
		var stateAfter = await harness.Service.GetById(stateVar.Id);
		var stateLabelAfter = await harness.Service.GetById(stateLabelVar.Id);

		Assert.Multiple(() =>
		{
			Assert.That(updateResult.Success,
				Is.False,
				"a widget-owned variable is written by its widget, not from outside");
			Assert.That(deleteResult.Success, Is.False, "a widget-owned variable cannot be deleted as a user variable");
			Assert.That(stateAfter!.Value, Is.EqualTo("on"), "value unchanged");
			Assert.That(stateLabelAfter, Is.Not.Null, "not deleted");
			Assert.That(scoped.Any(v => v.Name == "toggled"), Is.False, "no vars.toggled exists any more");
		});
	}

	// Scenario E2: the reconciler is the sole writer of vars.state/vars.state_label, so it must also be
	// what removes them once State Mode turns off - matching how vars.toggled was removed by the
	// retired ActionButtonStateVariableSync whenever a button stopped being an unbound toggle.
	[Test]
	public async Task StateVariables_AppearWithStateModeAndAreRemovedWhenItIsDisabled()
	{
		var widget = new WidgetEntity
		{
			Id = Guid.NewGuid(),
			FolderId = Guid.NewGuid(),
			Type = WidgetTypeIds.ActionButton,
			Data
				= "{\"stateMode\":true,\"states\":[{\"id\":\"off\",\"label\":\"Off\"},{\"id\":\"on\",\"label\":\"On\"}]," +
				"\"activeStateId\":\"on\"}"
		};
		var folderCache = new SingleWidgetFolderCache(widget);
		await using var harness = new VariableServiceHarness();

		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		var renderer = new VariableTemplateRenderer(harness.Registry);
		var stateService = new WidgetStateService(folderCache,
			renderer,
			new ActionConditionEvaluator(renderer),
			new FakeIntegrationRegistry(),
			new WidgetDerivedStateStore(),
			readiness);
		var reconciler = new WidgetStateReconciler(stateService,
			new WidgetDerivedStateStore(),
			harness.Service,
			new NoOpFlowExecutor(),
			folderCache,
			new NotSupportedWidgetService(),
			new WidgetDataWriteLock(),
			TestLocalization.Preferences,
			TestLocalization.Resolver,
			Serilog.Log.Logger);

		await reconciler.Reconcile(widget.Id);
		Assert.That(await harness.Service.GetByScope(VariableScope.Widget, widget.Id.ToString()),
			Has.Some.Matches<VariableEntity>(v => v.Name == "state"),
			"precondition: vars.state exists while State Mode is on");

		widget.Data = "{}"; // State Mode disabled
		await reconciler.Reconcile(widget.Id);

		var afterDisable = await harness.Service.GetByScope(VariableScope.Widget, widget.Id.ToString());
		Assert.Multiple(() =>
		{
			Assert.That(afterDisable.Any(v => v.Name == "state"),
				Is.False,
				"vars.state must not survive State Mode being disabled");
			Assert.That(afterDisable.Any(v => v.Name == "state_label"),
				Is.False,
				"vars.state_label must not survive State Mode being disabled");
		});
	}

	[Test]
	public async Task PlainTransition_DoesNotCarryStates_ButASetChangePushesTheNewOnes()
	{
		var transport = new RecordingTransport();
		var subscriptions = new WidgetStateSubscriptionTracker();
		var publisher = new WidgetStatePublisher(transport, subscriptions, new WidgetRenderSignals());
		var widgetId = Guid.NewGuid();
		subscriptions.Add("conn-1", widgetId.ToString());

		var plainTransition = new WidgetStateReconciliation("on",
			"On",
			[new WidgetStateOption("off", "Off"), new WidgetStateOption("on", "On")],
			Changed: true,
			Transitioned: true,
			SetChanged: false);
		await publisher.PublishIfChanged(widgetId, plainTransition);

		var setChange = new WidgetStateReconciliation("live",
			"Live",
			[new WidgetStateOption("live", "Live"), new WidgetStateOption("offline", "Offline")],
			Changed: true,
			Transitioned: true,
			SetChanged: true);
		await publisher.PublishIfChanged(widgetId, setChange);

		Assert.Multiple(() =>
		{
			Assert.That(transport.Sent[0].States, Is.Null, "a plain transition does not carry the state list");
			Assert.That(transport.Sent[1].States,
				Is.Not.Null.And.Count.EqualTo(2),
				"a set change pushes the new states");
		});
	}

	// The render-signal seam beside PublishIfChanged's client push: an open in-process session must
	// see exactly the same event a legacy client is pushed, in the same call.
	[Test]
	public async Task PublishIfChanged_RaisesTheRenderSignal_WithTheExactPayloadPushedToClients()
	{
		var transport = new RecordingTransport();
		var subscriptions = new WidgetStateSubscriptionTracker();
		var renderSignals = new RecordingRenderSignals();
		var publisher = new WidgetStatePublisher(transport, subscriptions, renderSignals);
		var widgetId = Guid.NewGuid();
		subscriptions.Add("conn-1", widgetId.ToString());

		var result = new WidgetStateReconciliation("on",
			"On",
			[new WidgetStateOption("off", "Off"), new WidgetStateOption("on", "On")],
			Changed: true,
			Transitioned: true,
			SetChanged: true);

		await publisher.PublishIfChanged(widgetId, result);

		Assert.That(renderSignals.StateChanges, Has.Count.EqualTo(1), "the render signal must fire once");
		Assert.That(transport.Sent, Has.Count.EqualTo(1), "the client push must fire once");

		var raised = renderSignals.StateChanges[0];
		var pushed = transport.Sent[0];
		Assert.Multiple(() =>
		{
			Assert.That(raised.WidgetId, Is.EqualTo(pushed.WidgetId));
			Assert.That(raised.StateId, Is.EqualTo(pushed.StateId));
			Assert.That(raised.StateLabel, Is.EqualTo(pushed.StateLabel));
			Assert.That(raised.States, Is.EqualTo(pushed.States));
		});
	}

	// Naive "always raise" would miss this: PublishIfChanged gates the client push on an unchanged
	// state that also didn't change its set, so the render signal must be gated the exact same way -
	// otherwise an open session would redraw for a transition a legacy client never even saw.
	[Test]
	public async Task PublishIfChanged_DoesNotRaiseTheRenderSignal_WhenTheClientPushIsGatedByUnchangedState()
	{
		var transport = new RecordingTransport();
		var subscriptions = new WidgetStateSubscriptionTracker();
		var renderSignals = new RecordingRenderSignals();
		var publisher = new WidgetStatePublisher(transport, subscriptions, renderSignals);
		var widgetId = Guid.NewGuid();
		subscriptions.Add("conn-1", widgetId.ToString());

		var unchanged = new WidgetStateReconciliation("on",
			"On",
			[new WidgetStateOption("off", "Off"), new WidgetStateOption("on", "On")],
			Changed: false,
			Transitioned: false,
			SetChanged: false);

		await publisher.PublishIfChanged(widgetId, unchanged);

		Assert.That(transport.Sent, Is.Empty, "precondition: the client push is gated when nothing changed");
		Assert.That(renderSignals.StateChanges, Is.Empty, "the render signal must be gated the same way");
	}

	[Test]
	public async Task Publisher_DropsAnOptimisticResultReplacedByANewerGeneration()
	{
		var transport = new RecordingTransport();
		var subscriptions = new WidgetStateSubscriptionTracker();
		var optimisticStates = new WidgetOptimisticStateStore(TimeProvider.System);
		var publisher = new WidgetStatePublisher(transport, subscriptions, new WidgetRenderSignals(), optimisticStates);
		var widgetId = Guid.NewGuid();
		var identity = new WidgetOptimisticStateIdentity(widgetId, "provider", "integration", "toggle");
		var staleGeneration = optimisticStates.Begin(identity);
		optimisticStates.TryApply(identity, staleGeneration, "off");
		var staleVersion = optimisticStates.Observe(identity);
		var staleResult = new WidgetStateReconciliation("off", "Off", [], true, true, false)
		{
			OptimisticState = staleVersion
		};
		subscriptions.Add("conn-1", widgetId.ToString());
		optimisticStates.TryApply(identity, optimisticStates.Begin(identity), "on");

		await publisher.PublishIfChanged(widgetId, staleResult);

		Assert.That(transport.Sent, Is.Empty);
	}

	private sealed class VariableServiceHarness : IAsyncDisposable
	{
		private readonly ServiceProvider _provider;
		private readonly IServiceScope _scope;

		public VariableServiceHarness()
		{
			var services = new ServiceCollection();
			services.AddSingleton<VariableRegistry>();
			services.AddSingleton<IMediator, RecordingMediator>();
			services.AddSingleton<IUserVariableStore, NullUserVariableStore>();
			services.AddTestVariableService();
			_provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
			_scope = _provider.CreateScope();
			Registry = _provider.GetRequiredService<VariableRegistry>();
			Service = _scope.ServiceProvider.GetRequiredService<IVariableService>();
		}

		public VariableRegistry Registry { get; }
		public IVariableService Service { get; }

		public ValueTask DisposeAsync()
		{
			_scope.Dispose();
			_provider.Dispose();
			return ValueTask.CompletedTask;
		}
	}

	private sealed class NullUserVariableStore : IUserVariableStore
	{
		public IReadOnlyList<VariableEntity> Load() => [];

		public void Save(IEnumerable<VariableEntity> userVariables)
		{
		}
	}

	private sealed class NoOpFlowExecutor : IFlowExecutor
	{
		public Task<FlowExecutionResult> ExecuteAsync(FlowExecutionRequest request, CancellationToken cancellationToken)
			=> Task.FromResult(new FlowExecutionResult
			{
				ExecutionId = Guid.NewGuid(), Status = FlowExecutionStatus.Succeeded, MatchedFlows = 0
			});
	}

	private sealed class NotSupportedWidgetService : IWidgetService
	{
		public Task<Result<WidgetEntity, WidgetError>> Update(WidgetEntity widget) => throw new NotSupportedException();

		public Task<Result<WidgetEntity, WidgetError>> Create(Guid folderId,
			WidgetEntity widget,
			Guid? sourceWidgetId = null)
			=> throw new NotSupportedException();

		public Task<Result<List<WidgetEntity>, WidgetError>> UpdatePositions(
			Guid folderId,
			IReadOnlyList<WidgetPlacement> placements)
			=> throw new NotSupportedException();

		public Task<Result<WidgetError>> Delete(Guid widgetId, Guid folderId) => throw new NotSupportedException();

		public Task<Result<WidgetEntity, WidgetError>> SetPinned(Guid folderId,
			Guid widgetId,
			bool pinned,
			PinScope? scope = null)
			=> throw new NotSupportedException();

		public Task<Result<List<WidgetEntity>, WidgetError>> CreateMany(Guid folderId,
			IReadOnlyList<WidgetEntity> widgets,
			IReadOnlyList<Guid>? replaceIds = null,
			IReadOnlyList<Guid?>? sourceWidgetIds = null)
			=> throw new NotSupportedException();

		public Task<Result<WidgetError>> DeleteMany(Guid folderId, IReadOnlyList<Guid> widgetIds)
			=> throw new NotSupportedException();

		public Task<Result<List<WidgetEntity>, WidgetError>> SetPinnedMany(
			Guid folderId,
			IReadOnlyList<Guid> widgetIds,
			bool pinned,
			PinScope? scope = null)
			=> throw new NotSupportedException();
	}

	private sealed class SingleWidgetFolderCache : IFolderCache
	{
		private readonly FolderEntity _folder;

		public SingleWidgetFolderCache(WidgetEntity widget)
			=> _folder = new FolderEntity { Id = widget.FolderId, Name = "f", Order = 0, Widgets = [widget] };

		public FolderEntity? GetFolderById(Guid id) => id == _folder.Id ? _folder : null;
		public List<FolderEntity> GetAllFolders() => [_folder];
		public Task InitializeCache() => Task.CompletedTask;
		public List<FolderEntity> GetFoldersByParentId(Guid? parentId) => [_folder];
		public List<FolderEntity> GetFoldersByProfileId(Guid profileId) => [_folder];
		public Task AddOrUpdate(FolderEntity folder) => Task.CompletedTask;

		public Task AddOrUpdateRange(IReadOnlyCollection<FolderEntity> folders) => Task.CompletedTask;

		public Task<FolderSubtreeRemoval> RemoveSubtree(Guid rootId)
			=> Task.FromResult(new FolderSubtreeRemoval(false, [], []));

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

	private sealed class RecordingTransport : IUiTransport
	{
		public List<WidgetStateUpdatedEvent> Sent { get; } = [];

		public Task SendToConnection<T>(string connectionId, T message, CancellationToken cancellationToken = default)
			where T : class
			=> Task.CompletedTask;

		public Task AddToGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task RemoveFromGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task Send<T>(T message, CancellationToken cancellationToken = default)
			where T : class
			=> Task.CompletedTask;

		public Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
			where T : class
		{
			if (message is WidgetStateUpdatedEvent evt)
			{
				Sent.Add(evt);
			}

			return Task.CompletedTask;
		}
	}
}
