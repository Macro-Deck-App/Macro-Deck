using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;
using WidgetServiceImpl = MacroDeckHost.Application.Services.WidgetService;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class UpdateWidgetPositionsRequestMessageHandlerTests
{
	private ProfileCache _cache = null!;
	private UpdateWidgetPositionsRequestMessageHandler _handler = null!;
	private Guid _folderId;
	private Guid _widgetId;

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
		_handler = new UpdateWidgetPositionsRequestMessageHandler(service);

		var profileId = Guid.NewGuid();
		_folderId = Guid.NewGuid();
		_widgetId = Guid.NewGuid();
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
		_cache.AddWidget(_folderId,
			new WidgetEntity
			{
				Id = _widgetId,
				FolderId = _folderId,
				Type = WidgetTypeIds.ActionButton,
				Data = "{\"label\":\"x\"}"
			});
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task InvalidFolderGuid_ReturnsValidationError()
	{
		var response = await _handler.Handle(new UpdateWidgetPositionsRequest { FolderId = "not-a-guid" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo("VALIDATION_ERROR"));
		});
	}

	[Test]
	public async Task InvalidWidgetGuid_ReturnsValidationError()
	{
		var response = await _handler.Handle(new UpdateWidgetPositionsRequest
			{
				FolderId = _folderId.ToString(),
				Positions =
				[
					new WidgetPositionUpdate { Id = "nope", PositionX = 1, PositionY = 1, Width = 1, Height = 1 }
				]
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo("VALIDATION_ERROR"));
		});
	}

	[Test]
	public async Task ServiceError_IsMappedToTransportError()
	{
		var response = await _handler.Handle(new UpdateWidgetPositionsRequest
			{
				FolderId = Guid.NewGuid().ToString(),
				Positions = [Position(_widgetId, 1, 1)]
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(WidgetError.FolderNotFound)));
		});
	}

	[Test]
	public async Task ValidRequest_MapsUpdatedWidgetsToDtos()
	{
		var response = await _handler.Handle(new UpdateWidgetPositionsRequest
			{
				FolderId = _folderId.ToString(),
				Positions = [Position(_widgetId, 2, 3)]
			},
			CancellationToken.None);

		Assert.That(response.Success, Is.True);
		var widget = response.Widgets!.Single();
		Assert.Multiple(() =>
		{
			Assert.That(widget.Id, Is.EqualTo(_widgetId.ToString()));
			Assert.That(widget.PositionX, Is.EqualTo(2));
			Assert.That(widget.PositionY, Is.EqualTo(3));
			Assert.That(widget.Data, Is.EqualTo("{\"label\":\"x\"}"));
		});
	}

	private static WidgetPositionUpdate Position(Guid id, int x, int y) => new()
	{
		Id = id.ToString(),
		PositionX = x,
		PositionY = y,
		Width = 1,
		Height = 1
	};
}
