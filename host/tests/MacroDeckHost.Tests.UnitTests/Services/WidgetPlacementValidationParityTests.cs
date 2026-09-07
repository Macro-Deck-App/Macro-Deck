using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;
using WidgetServiceImpl = MacroDeckHost.Application.Services.WidgetService;

namespace MacroDeckHost.Tests.UnitTests.Services;

/// <summary>
/// A placement the grid cannot hold is refused the same way whichever write path a client reaches
/// for - creating a widget, creating a batch, moving one, or saving one from the editor (issue #778).
/// </summary>
[TestFixture]
public class WidgetPlacementValidationParityTests
{
	private const int Columns = 5;
	private const int Rows = 3;

	private ProfileCache _cache = null!;
	private CreateWidgetRequestMessageHandler _create = null!;
	private CreateWidgetsRequestMessageHandler _createMany = null!;
	private UpdateWidgetRequestMessageHandler _update = null!;
	private UpdateWidgetPositionsRequestMessageHandler _move = null!;
	private Guid _folderId;

	[SetUp]
	public async Task SetUp()
	{
		_cache = new ProfileCache(new InMemoryProfileStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		var secretService = new FakeSecretService();
		var service = new WidgetServiceImpl(new FolderCache(_cache),
			_cache,
			new RecordingMediator(),
			new WidgetSecretScrubber(secretService),
			new WidgetSecretCloner(secretService),
			new NullWidgetVariableCloner());
		_create = new CreateWidgetRequestMessageHandler(service);
		_createMany = new CreateWidgetsRequestMessageHandler(service);
		_update = new UpdateWidgetRequestMessageHandler(service,
			new WidgetDataSchemaProvider(new WidgetTypeRegistry(new RecordingMediator())),
			new FolderCache(_cache));
		_move = new UpdateWidgetPositionsRequestMessageHandler(service);

		var profileId = Guid.NewGuid();
		_folderId = Guid.NewGuid();
		await _cache.AddOrUpdate(new ProfileEntity { Id = profileId, Name = "P" });
		await _cache.AddOrUpdateFolder(new FolderEntity
		{
			Id = _folderId,
			ProfileId = profileId,
			Name = "F",
			Order = 0,
			Rows = Rows,
			Columns = Columns
		});
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task CreateOutsideTheGrid_IsRefusedWithTheSameErrorAsTheEquivalentMove()
	{
		var anchor = await CreateAt(0, 0);

		// 2 wide starting at the last column: the widget would hang one column past the grid.
		var created = await _create.Handle(CreateRequest(Columns - 1, 0, 2, 2),
			CancellationToken.None);
		var moved = await _move.Handle(new UpdateWidgetPositionsRequest
			{
				FolderId = _folderId.ToString(),
				Positions =
				[
					new WidgetPositionUpdate
					{
						Id = anchor.ToString(),
						PositionX = Columns - 1,
						PositionY = 0,
						Width = 2,
						Height = 2
					}
				]
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(moved.Success, Is.False);
			Assert.That(created.Success, Is.False);
			Assert.That(created.Error!.Code, Is.EqualTo(moved.Error!.Code));
			Assert.That(TestLocalization.Resolve(created.Error!.Message),
				Is.EqualTo(TestLocalization.Resolve(moved.Error!.Message)));
			Assert.That(Widgets().Select(w => w.Id), Is.EquivalentTo(new[] { anchor }));
		});
	}

	[Test]
	public async Task BatchCreateOutsideTheGrid_IsRefusedLikeTheSingleCreate()
	{
		var single = await _create.Handle(CreateRequest(0, Rows, 1, 1),
			CancellationToken.None);
		var batch = await _createMany.Handle(new CreateWidgetsRequest
			{
				FolderId = _folderId.ToString(),
				Widgets =
				[
					new CreateWidgetItem
					{
						Type = WidgetTypeIds.Clock,
						PositionX = 0,
						PositionY = Rows,
						Width = 1,
						Height = 1
					}
				]
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(single.Success, Is.False);
			Assert.That(batch.Success, Is.False);
			Assert.That(single.Error!.Code, Is.EqualTo(batch.Error!.Code));
			Assert.That(Widgets(), Is.Empty);
		});
	}

	[Test]
	public async Task CreateOnTopOfAnExistingWidget_IsRefusedAsOccupied()
	{
		var existing = await CreateAt(1, 1);

		var response = await _create.Handle(CreateRequest(1, 1, 1, 1), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(WidgetError.PositionOccupied)));
			Assert.That(Widgets().Select(w => w.Id), Is.EquivalentTo(new[] { existing }));
		});
	}

	[Test]
	public async Task CreateInsideTheGridOnFreeCells_Succeeds()
	{
		var response = await _create.Handle(CreateRequest(Columns - 2,
				Rows - 2,
				2,
				2),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(Widgets(), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task EditorSaveThatMovesAWidgetOutsideTheGrid_IsRefused()
	{
		var widgetId = await CreateAt(0, 0);

		var response = await _update.Handle(UpdateRequest(widgetId,
				Columns - 1,
				0,
				2,
				2),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(WidgetError.ValidationError)));
			Assert.That(Widgets().Single().PositionX, Is.EqualTo(0));
		});
	}

	[Test]
	public async Task StoredWidgetOutsideTheGrid_StillAcceptsAWriteThatLeavesItsRectangleAlone()
	{
		// A grid shrink can strand a widget outside the grid. Nothing rejects it on load, and the data
		// and appearance writes that keep flowing to it must not start failing either.
		var widgetId = Guid.NewGuid();
		_cache.AddWidget(_folderId,
			new WidgetEntity
			{
				Id = widgetId,
				FolderId = _folderId,
				Type = WidgetTypeIds.Clock,
				PositionX = Columns + 4,
				PositionY = Rows + 4,
				Width = 2,
				Height = 2
			});

		var request = UpdateRequest(widgetId,
			Columns + 4,
			Rows + 4,
			2,
			2);
		request.Data = "{\"showSeconds\":true}";

		var response = await _update.Handle(request, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(Widgets().Single().Data, Is.EqualTo("{\"showSeconds\":true}"));
		});
	}

	private async Task<Guid> CreateAt(int x, int y)
	{
		var response = await _create.Handle(CreateRequest(x, y, 1, 1), CancellationToken.None);
		Assert.That(response.Success, Is.True, "the fixture's own widget must be a legal placement");
		return Guid.Parse(response.Widget!.Id);
	}

	private CreateWidgetRequest CreateRequest(int x, int y, int width, int height) => new()
	{
		FolderId = _folderId.ToString(),
		Type = WidgetTypeIds.Clock,
		PositionX = x,
		PositionY = y,
		Width = width,
		Height = height
	};

	private UpdateWidgetRequest UpdateRequest(Guid widgetId, int x, int y, int width, int height) => new()
	{
		Id = widgetId.ToString(),
		FolderId = _folderId.ToString(),
		Type = WidgetTypeIds.Clock,
		PositionX = x,
		PositionY = y,
		Width = width,
		Height = height
	};

	private List<WidgetEntity> Widgets() => _cache.GetFolderById(_folderId)!.Widgets;
}
