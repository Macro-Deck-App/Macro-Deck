using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class WidgetServiceUpdatePositionsTests
{
	private InMemoryProfileStore _store = null!;
	private ProfileCache _cache = null!;
	private RecordingMediator _mediator = null!;
	private WidgetService _service = null!;
	private Guid _folderId;
	private WidgetEntity _widgetA = null!;
	private WidgetEntity _widgetB = null!;

	[SetUp]
	public async Task SetUp()
	{
		_store = new InMemoryProfileStore();
		_cache = new ProfileCache(_store, new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		_mediator = new RecordingMediator();
		var secretService = new FakeSecretService();
		_service = new WidgetService(new FolderCache(_cache),
			_cache,
			_mediator,
			new WidgetSecretScrubber(secretService),
			new WidgetSecretCloner(secretService),
			new NullWidgetVariableCloner());

		var profileId = Guid.NewGuid();
		_folderId = Guid.NewGuid();
		await _cache.AddOrUpdate(new ProfileEntity { Id = profileId, Name = "P" });
		await _cache.AddOrUpdateFolder(new FolderEntity
		{
			Id = _folderId,
			ProfileId = profileId,
			Name = "F",
			Order = 0,
			Rows = 4,
			Columns = 4
		});

		_widgetA = Widget(0, 0);
		_widgetB = Widget(1, 0);
		_cache.AddWidget(_folderId, _widgetA);
		_cache.AddWidget(_folderId, _widgetB);
		_mediator.Published.Clear();
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task UnknownFolder_FailsWithFolderNotFound()
	{
		var result = await _service.UpdatePositions(Guid.NewGuid(), [Placement(_widgetA, 2, 2)]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.FolderNotFound));
		});
	}

	[Test]
	public async Task UnknownWidget_FailsWithNotFoundAndDoesNotMutate()
	{
		var result = await _service.UpdatePositions(_folderId,
		[
			Placement(_widgetA, 2, 2),
			new WidgetPlacement(Guid.NewGuid(), 3, 3, 1, 1)
		]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.NotFound));
			Assert.That(_widgetA.PositionX, Is.EqualTo(0));
			Assert.That(_mediator.Published, Is.Empty);
		});
	}

	[Test]
	public async Task OutOfBoundsPlacement_FailsWithValidationErrorAndDoesNotMutate()
	{
		var result = await _service.UpdatePositions(_folderId, [new WidgetPlacement(_widgetA.Id, 3, 3, 2, 2)]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.ValidationError));
			Assert.That(_widgetA.PositionX, Is.EqualTo(0));
			Assert.That(_mediator.Published, Is.Empty);
		});
	}

	[Test]
	public async Task ZeroSizePlacement_FailsWithValidationError()
	{
		var result = await _service.UpdatePositions(_folderId, [new WidgetPlacement(_widgetA.Id, 0, 0, 0, 1)]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.ValidationError));
		});
	}

	[Test]
	public async Task DuplicateIds_FailWithValidationError()
	{
		var result = await _service.UpdatePositions(_folderId,
		[
			Placement(_widgetA, 2, 2),
			Placement(_widgetA, 3, 3)
		]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.ValidationError));
		});
	}

	[Test]
	public async Task OverlapWithUntouchedWidget_FailsWithPositionOccupiedAndDoesNotMutate()
	{
		var result = await _service.UpdatePositions(_folderId, [Placement(_widgetA, 1, 0)]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.PositionOccupied));
			Assert.That(_widgetA.PositionX, Is.EqualTo(0));
			Assert.That(_mediator.Published, Is.Empty);
		});
	}

	[Test]
	public async Task OverlapWithinBatch_FailsWithPositionOccupied()
	{
		var result = await _service.UpdatePositions(_folderId,
		[
			Placement(_widgetA, 2, 2),
			Placement(_widgetB, 2, 2)
		]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.PositionOccupied));
		});
	}

	[Test]
	public async Task PreExistingOverlapAmongUntouchedWidgets_DoesNotBlockTheBatch()
	{
		var overlapping1 = Widget(3, 3);
		var overlapping2 = Widget(3, 3);
		_cache.AddWidget(_folderId, overlapping1);
		_cache.AddWidget(_folderId, overlapping2);
		_mediator.Published.Clear();

		var result = await _service.UpdatePositions(_folderId, [Placement(_widgetA, 0, 1)]);

		Assert.That(result.Success, Is.True);
	}

	[Test]
	public async Task ValidBatch_MutatesPositionsOnlyAndPersists()
	{
		_widgetA.Data = "{\"label\":\"keep\"}";

		var result = await _service.UpdatePositions(_folderId,
		[
			Placement(_widgetA, 2, 2),
			Placement(_widgetB, 0, 0)
		]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_widgetA.PositionX, Is.EqualTo(2));
			Assert.That(_widgetA.PositionY, Is.EqualTo(2));
			Assert.That(_widgetA.Data, Is.EqualTo("{\"label\":\"keep\"}"));
			Assert.That(_widgetB.PositionX, Is.EqualTo(0));
		});
	}

	[Test]
	public async Task ValidBatch_PublishesOneNotificationWithSizeChangedIds()
	{
		await _service.UpdatePositions(_folderId,
		[
			Placement(_widgetA, 2, 2),
			new WidgetPlacement(_widgetB.Id, 0, 1, 2, 2)
		]);

		var notification = _mediator.Published.OfType<WidgetPositionsUpdatedNotification>().Single();
		Assert.Multiple(() =>
		{
			Assert.That(_mediator.Published, Has.Count.EqualTo(1));
			Assert.That(notification.FolderId, Is.EqualTo(_folderId));
			Assert.That(notification.Widgets.Select(w => w.Id),
				Is.EquivalentTo([_widgetA.Id, _widgetB.Id]));
			Assert.That(notification.SizeChangedWidgetIds, Is.EquivalentTo([_widgetB.Id]));
		});
	}

	[Test]
	public async Task EmptyBatch_SucceedsWithoutNotification()
	{
		var result = await _service.UpdatePositions(_folderId, []);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_mediator.Published, Is.Empty);
		});
	}

	private static WidgetEntity Widget(int x, int y) => new()
	{
		Id = Guid.NewGuid(),
		Type = WidgetTypeIds.ActionButton,
		PositionX = x,
		PositionY = y,
		Width = 1,
		Height = 1
	};

	private static WidgetPlacement Placement(WidgetEntity widget, int x, int y)
		=> new(widget.Id, x, y, widget.Width, widget.Height);
}
