using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Ui;

[TestFixture]
public class UpdateWidgetSchemaValidationTests
{
	private static UpdateWidgetRequestMessageHandler CreateHandler(RecordingWidgetService service)
		=> new(service, new WidgetDataSchemaProvider(new WidgetTypeRegistry(new RecordingMediator())));

	private static UpdateWidgetRequest Request(string type, string? data) => new()
	{
		Id = Guid.NewGuid().ToString(),
		FolderId = Guid.NewGuid().ToString(),
		Type = type,
		Data = data
	};

	[Test]
	public async Task Schema_invalid_data_is_rejected_before_it_reaches_the_widget_service()
	{
		var service = new RecordingWidgetService();
		var handler = CreateHandler(service);

		var response
			= await handler.Handle(Request(WidgetTypeIds.Clock, "{\"style\":\"sundial\"}"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error, Is.Not.Null);
			Assert.That(response.Error!.Code, Is.EqualTo("VALIDATION_ERROR"));
			Assert.That(TestLocalization.Resolve(response.Error!.Message), Is.Not.Empty);
			Assert.That(TestLocalization.Resolve(response.Error!.Message), Does.Contain("style"));
			// Load-bearing: a check that fails *after* already calling Update would corrupt the store
			// while looking correct from the response alone.
			Assert.That(service.Updated, Is.Null, "a rejected update must never reach the widget service");
		});
	}

	[Test]
	public void Malformed_JSON_is_a_structured_error_not_an_exception()
	{
		var service = new RecordingWidgetService();
		var handler = CreateHandler(service);
		UpdateWidgetResponse? response = null;

		Assert.DoesNotThrowAsync(async () =>
			response = await handler.Handle(Request(WidgetTypeIds.Clock, "{\"style\":"), CancellationToken.None));

		Assert.Multiple(() =>
		{
			Assert.That(response, Is.Not.Null);
			Assert.That(response!.Success, Is.False);
			Assert.That(response.Error, Is.Not.Null);
			Assert.That(response.Error!.Code, Is.EqualTo("VALIDATION_ERROR"));
			Assert.That(service.Updated, Is.Null);
		});
	}

	[TestCase(null)]
	[TestCase("{}")]
	public async Task Absent_and_empty_data_still_save(string? data)
	{
		var service = new RecordingWidgetService();
		var handler = CreateHandler(service);

		var response = await handler.Handle(Request(WidgetTypeIds.Clock, data), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(service.Updated, Is.Not.Null);
		});
	}

	// Scenario B5: an unresolvable fallback is an authoring error, not something to guess at - the
	// save is rejected outright and the previously stored mapping (simulated here by never reaching
	// the widget service) is left byte-identical. This only applies to a mapping that is actually
	// authoritative (carries a usable rule); see the case below for the rules-less shape.
	[TestCase(
		"{\"stateMode\":true,\"states\":[{\"id\":\"off\",\"label\":\"Off\"}],\"stateMapping\":{\"rules\":[{\"id\":\"r1\",\"stateId\":\"off\",\"when\":{\"kind\":\"compare\",\"left\":{\"$var\":\"cpu\"},\"operator\":\">=\",\"right\":90}}]}}")]
	[TestCase(
		"{\"stateMode\":true,\"states\":[{\"id\":\"off\",\"label\":\"Off\"}],\"stateMapping\":{\"rules\":[{\"id\":\"r1\",\"stateId\":\"off\",\"when\":{\"kind\":\"compare\",\"left\":{\"$var\":\"cpu\"},\"operator\":\">=\",\"right\":90}}],\"fallbackStateId\":\"missing\"}}")]
	public async Task MappingWithoutExactlyOneResolvableFallback_IsRejectedOnSave(string data)
	{
		var service = new RecordingWidgetService();
		var handler = CreateHandler(service);

		var response = await handler.Handle(Request(WidgetTypeIds.ActionButton, data), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error, Is.Not.Null);
			Assert.That(service.Updated, Is.Null, "the previously stored mapping must stay byte-identical");
		});
	}

	// Regression for issue #718: a rules-less stateMapping is never authoritative (see
	// ActionButtonStateJson.PruneDegenerateMapping), so it is pruned by Normalize before
	// HasResolvableFallback ever runs - an unresolvable fallback on a mapping that can never be
	// consulted anyway must not block the save.
	[TestCase("{\"stateMode\":true,\"states\":[{\"id\":\"off\",\"label\":\"Off\"}],\"stateMapping\":{\"rules\":[]}}")]
	[TestCase(
		"{\"stateMode\":true,\"states\":[{\"id\":\"off\",\"label\":\"Off\"}],\"stateMapping\":{\"rules\":[],\"fallbackStateId\":\"missing\"}}")]
	public async Task RulesLessMappingWithAnUnresolvableFallback_SavesSuccessfully(string data)
	{
		var service = new RecordingWidgetService();
		var handler = CreateHandler(service);

		var response = await handler.Handle(Request(WidgetTypeIds.ActionButton, data), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(service.Updated, Is.Not.Null);
		});
	}

	[Test]
	public async Task Restyling_an_alias_only_toggle_bag_saves_successfully()
	{
		var data = WidgetAppearanceJson.ParseDataBag(
			"""{"mode":"toggle","offState":{"label":"Off"},"onState":{"label":"On"}}""");
		WidgetAppearanceJson.Apply(data,
			WidgetTypeIds.ActionButton,
			new WidgetAppearancePatch { BackgroundColor = "#123456" },
			["on"]);

		var service = new RecordingWidgetService();
		var handler = CreateHandler(service);

		var response
			= await handler.Handle(Request(WidgetTypeIds.ActionButton, data.ToJsonString()), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True, TestLocalization.Resolve(response.Error?.Message ?? default));
			Assert.That(service.Updated, Is.Not.Null);
		});
	}

	[Test]
	public async Task Runtime_data_patch_is_not_gated_by_schema_validation()
	{
		var folderId = Guid.NewGuid();
		var widget = new WidgetEntity
		{
			Id = Guid.NewGuid(),
			FolderId = folderId,
			Type = WidgetTypeIds.ActionButton,
			Data = "{\"label\":\"x\",\"imageUrl\":\"http://example/x.png\"}"
		};

		var service = new RecordingWidgetService();
		var handler = new UpdateWidgetDataRequestMessageHandler(new FakeFolderCache(folderId, widget),
			service,
			new NoOpFlowExecutor(),
			new WidgetDataWriteLock(),
			Serilog.Log.Logger);

		var response = await handler.Handle(new UpdateWidgetDataRequest
			{
				WidgetId = widget.Id.ToString(),
				FolderId = folderId.ToString(),
				Data = "{\"isToggled\":true}"
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(service.Updated, Is.Not.Null);
		});
	}

	private sealed class NoOpFlowExecutor : IFlowExecutor
	{
		public Task<FlowExecutionResult> ExecuteAsync(FlowExecutionRequest request, CancellationToken cancellationToken)
			=> Task.FromResult(new FlowExecutionResult
			{
				ExecutionId = Guid.NewGuid(),
				Status = FlowExecutionStatus.Succeeded,
				MatchedFlows = 0
			});
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

		public Task<Result<List<WidgetEntity>, WidgetError>> UpdatePositions(Guid folderId,
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
