using System.Text.Json.Nodes;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeckHost.Tests.UnitTests.Ui;

[TestFixture]
public class UpdateWidgetDataRuntimeGuardTests
{
	private const string BoundToggle =
		"{\"mode\":\"toggle\",\"stateBinding\":{\"kind\":\"compare\",\"id\":\"c\"," +
		"\"left\":{\"$var\":\"cpu\"},\"operator\":\">=\",\"right\":80}}";

	private static async Task<(WidgetEntity Updated, RecordingFlowExecutor Flow)> Patch(
		string initialData,
		string updateJson)
	{
		var folderId = Guid.NewGuid();
		var widget = new WidgetEntity
		{
			Id = Guid.NewGuid(),
			FolderId = folderId,
			Type = WidgetTypeIds.ActionButton,
			Data = initialData
		};

		var service = new RecordingWidgetService();
		var flow = new RecordingFlowExecutor();
		var handler = new UpdateWidgetDataRequestMessageHandler(new FakeFolderCache(folderId, widget),
			service,
			flow,
			new WidgetDataWriteLock(),
			Serilog.Log.Logger);

		var response = await handler.Handle(new UpdateWidgetDataRequest
			{
				WidgetId = widget.Id.ToString(),
				FolderId = folderId.ToString(),
				Data = updateJson
			},
			CancellationToken.None);

		Assert.That(response.Success, Is.True);
		Assert.That(service.Updated, Is.Not.Null);
		return (service.Updated!, flow);
	}

	[Test]
	public async Task AFlowThatWritesTheSameWidget_DoesNotDeadlockThePress()
	{
		var folderId = Guid.NewGuid();
		var widget = new WidgetEntity
		{
			Id = Guid.NewGuid(),
			FolderId = folderId,
			Type = WidgetTypeIds.ActionButton,
			Data = "{\"mode\":\"toggle\"}"
		};

		var writeLock = new WidgetDataWriteLock();
		var handler = new UpdateWidgetDataRequestMessageHandler(new FakeFolderCache(folderId, widget),
			new RecordingWidgetService(),
			new ReentrantFlowExecutor(writeLock, widget.Id),
			writeLock,
			Serilog.Log.Logger);

		var press = handler.Handle(new UpdateWidgetDataRequest
			{
				WidgetId = widget.Id.ToString(),
				FolderId = folderId.ToString(),
				Data = "{\"isToggled\":true}"
			},
			CancellationToken.None).AsTask();

		var finished = await Task.WhenAny(press, Task.Delay(TimeSpan.FromSeconds(5)));
		Assert.That(finished, Is.SameAs(press), "the press deadlocked on its own write gate");
		Assert.That((await press).Success, Is.True);
	}

	[Test]
	public async Task BoundToggleButton_does_not_persist_isToggled()
	{
		var (updated, _) = await Patch(BoundToggle, "{\"isToggled\":true}");

		Assert.That(updated.Data, Does.Not.Contain("isToggled"));
	}

	// Replaces UnboundToggleButton_still_persists_isToggled: a press used to flip isToggled on an
	// unbound legacy toggle. That mechanism is retired. A press can still change state - via the
	// implicit advance in ExecuteActionButtonTriggerRequestMessageHandler - but this handler's own
	// UpdateWidgetData patch path is not that mechanism and must not move activeStateId on its own;
	// the button's own flow (independent of this handler) must keep running exactly as before.
	// "Make press inert" (silently dropping the flow along with the retired flip) must fail this test.
	[Test]
	public async Task Press_DoesNotChangeStateThroughTheDataPatchPath_ButStillRunsTheFlow()
	{
		const string flow = """
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
		var action = new CapturingActionDefinition();
		var registry = new FakeIntegrationRegistry();
		registry.Add(new FakeIntegration { Id = "integration", Actions = [action] });
		var executor = new FlowExecutor(registry,
			new FakeVariableTemplateRenderer(),
			new PassthroughConditionEvaluator(),
			new FakeSecretService(),
			new NullActionInteractions(),
			new NullUiInteractions(),
			new UserNotificationStore(),
			new MusicPlayerPollNudge(registry),
			new FakeHostLockState(),
			TestLocalization.Preferences,
			TestLocalization.Resolver,
			Serilog.Log.Logger);

		var initialData = "{\"stateMode\":true,\"states\":[{\"id\":\"off\",\"label\":\"Off\"}," +
			"{\"id\":\"on\",\"label\":\"On\"}],\"activeStateId\":\"off\",\"flows\":" +
			JsonNode.Parse(flow)!["flows"]!.ToJsonString() +
			"}";

		var folderId = Guid.NewGuid();
		var widget = new WidgetEntity
		{
			Id = Guid.NewGuid(),
			FolderId = folderId,
			Type = WidgetTypeIds.ActionButton,
			Data = initialData
		};

		var service = new RecordingWidgetService();
		var handler = new UpdateWidgetDataRequestMessageHandler(new FakeFolderCache(folderId, widget),
			service,
			new RecordingFlowExecutor(),
			new WidgetDataWriteLock(),
			Serilog.Log.Logger);

		var pressResult = await executor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = widget.Data,
				Trigger = TriggerSelector.ByType(WidgetTriggerTypes.ShortPress),
				Scope = VariableScope.Widget,
				ScopeRefId = widget.Id.ToString(),
				OwnerWidgetId = widget.Id
			},
			CancellationToken.None);

		var response = await handler.Handle(new UpdateWidgetDataRequest
			{
				WidgetId = widget.Id.ToString(),
				FolderId = folderId.ToString(),
				Data = "{\"activeStateId\":\"on\",\"isToggled\":true}"
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(pressResult.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
			Assert.That(action.ExecuteCount, Is.EqualTo(1), "the button's own flow must still run on press");
			Assert.That(response.Success, Is.True);
			Assert.That(service.Updated!.Data,
				Does.Not.Contain("\"activeStateId\":\"on\""),
				"the UpdateWidgetData patch path must never move the button's state on its own");
			Assert.That(service.Updated!.Data, Does.Contain("\"activeStateId\":\"off\""));
		});
	}

	// Replaces UnboundToggleFlip_fires_onStateChange_after_the_update: the old behaviour upgraded a
	// legacy toggle by rewriting it in place and firing onStateChange from the patch itself. Neither
	// happens from this handler any more - most importantly, nothing here may migrate the button by
	// injecting an action into the user's own flow. persisted `flows` must be byte-identical to what
	// was stored.
	[Test]
	public async Task UpgradedLegacyToggleButton_StopsFlippingOnPress_AndItsFlowsAreNotRewritten()
	{
		const string flowsJson = "[{\"triggerId\":\"t1\",\"triggerType\":\"onShortPress\",\"children\":" +
			"[{\"id\":\"b1\",\"type\":\"action\",\"blockType\":\"integration.capture\"," +
			"\"integrationId\":\"integration\",\"actionId\":\"capture\",\"parameters\":[]}]}]";
		var initialData = $"{{\"mode\":\"toggle\",\"isToggled\":false,\"flows\":{flowsJson}}}";
		var expectedFlows = JsonNode.Parse(flowsJson)!.ToJsonString();

		var (updated, _) = await Patch(initialData, "{\"isToggled\":true}");

		var persistedFlows = ((JsonObject)JsonNode.Parse(updated.Data!)!)
			["flows"]!.ToJsonString();
		Assert.Multiple(() =>
		{
			Assert.That(persistedFlows,
				Is.EqualTo(expectedFlows),
				"a press must never rewrite the user's flows - not even by injecting a compensating action");
			Assert.That(updated.Data,
				Does.Contain("\"isToggled\":false"),
				"a press on an upgraded legacy toggle must not flip its old isToggled bit");
		});
	}

	[Test]
	public async Task UnchangedToggle_does_not_fire_onStateChange()
	{
		var (_, flow) = await Patch("{\"mode\":\"toggle\",\"isToggled\":true}", "{\"isToggled\":true}");

		Assert.That(flow.Triggers, Is.Empty);
	}

	[Test]
	public async Task BoundToggle_does_not_fire_onStateChange_from_the_patch()
	{
		var (_, flow) = await Patch(BoundToggle, "{\"isToggled\":true}");

		Assert.That(flow.Triggers, Is.Empty);
	}

	private sealed class ReentrantFlowExecutor : IFlowExecutor
	{
		private readonly IWidgetDataWriteLock _writeLock;
		private readonly Guid _widgetId;

		public ReentrantFlowExecutor(IWidgetDataWriteLock writeLock, Guid widgetId)
		{
			_writeLock = writeLock;
			_widgetId = widgetId;
		}

		public async Task<FlowExecutionResult> ExecuteAsync(
			FlowExecutionRequest request,
			CancellationToken cancellationToken)
		{
			using var _ = await _writeLock.AcquireAsync(_widgetId, cancellationToken);

			return new FlowExecutionResult
			{
				ExecutionId = request.ExecutionId,
				Status = FlowExecutionStatus.Succeeded,
				MatchedFlows = 1
			};
		}
	}

	private sealed class RecordingFlowExecutor : IFlowExecutor
	{
		public List<string> Triggers { get; } = [];

		public Task<FlowExecutionResult> ExecuteAsync(FlowExecutionRequest request, CancellationToken cancellationToken)
		{
			Triggers.Add(request.Trigger.Value);
			return Task.FromResult(new FlowExecutionResult
			{
				ExecutionId = Guid.NewGuid(),
				Status = FlowExecutionStatus.Succeeded,
				MatchedFlows = 1
			});
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

	private sealed class RecordingWidgetService : IWidgetService
	{
		public WidgetEntity? Updated { get; private set; }

		public Task<Result<WidgetEntity, WidgetError>> Update(WidgetEntity widget)
		{
			Updated = widget;
			return Task.FromResult(Result.Ok<WidgetEntity, WidgetError>(widget));
		}

		public Task<Result<WidgetEntity, WidgetError>> Create(Guid folderId,
			WidgetEntity widget,
			Guid? sourceWidgetId = null)
			=> throw new NotSupportedException();

		public Task<Result<List<WidgetEntity>, WidgetError>> UpdatePositions(
			Guid folderId,
			IReadOnlyList<WidgetPlacement> placements)
			=> throw new NotSupportedException();

		public Task<Result<WidgetError>> Delete(Guid widgetId, Guid folderId)
			=> throw new NotSupportedException();

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

	private sealed class FakeFolderCache : IFolderCache
	{
		private readonly FolderEntity _folder;

		public FakeFolderCache(Guid folderId, params WidgetEntity[] widgets)
		{
			_folder = new FolderEntity { Id = folderId, Name = "f", Order = 0, Widgets = widgets.ToList() };
		}

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
}
