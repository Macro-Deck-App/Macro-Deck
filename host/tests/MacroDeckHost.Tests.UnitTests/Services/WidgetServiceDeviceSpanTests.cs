using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class WidgetServiceDeviceSpanTests
{
	private ProfileCache _cache = null!;
	private InMemoryDeviceRepository _devices = null!;
	private WidgetService _service = null!;
	private Guid _profileId;
	private Guid _folderId;

	[SetUp]
	public async Task SetUp()
	{
		_cache = new ProfileCache(new InMemoryProfileStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		_devices = new InMemoryDeviceRepository();
		var secrets = new FakeSecretService();
		_service = new WidgetService(new FolderCache(_cache),
			_cache,
			new RecordingMediator(),
			new WidgetSecretScrubber(secrets),
			new WidgetSecretCloner(secrets),
			new NullWidgetVariableCloner(),
			null,
			null,
			_devices);

		_profileId = Guid.NewGuid();
		_folderId = Guid.NewGuid();
		await _cache.AddOrUpdate(new ProfileEntity
			{ Id = _profileId, Name = "P", DefaultRows = 3, DefaultColumns = 5 });
		await _cache.AddOrUpdateFolder(new FolderEntity
		{
			Id = _folderId,
			ProfileId = _profileId,
			Name = "F",
			Order = 0,
			Rows = 3,
			Columns = 5
		});
	}

	[TearDown]
	public void TearDown()
	{
		_cache.Dispose();
	}

	[Test]
	public async Task Create_RefusesAWidgetWiderThanTheClaimingDeviceAllows()
	{
		ClaimWithSingleKeyWidgets();

		var result = await _service.Create(_folderId, Widget(0, 0, 2, 1));

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.SpanLimitedByDevice));
			Assert.That(result.ErrorMessage, Does.Contain("Stream Deck"));
		});
	}

	[Test]
	public async Task Create_AcceptsASingleKeyWidget()
	{
		ClaimWithSingleKeyWidgets();

		var result = await _service.Create(_folderId, Widget(0, 0, 1, 1));

		Assert.That(result.Success, Is.True);
	}

	[Test]
	public async Task Update_RefusesGrowingAWidget_ButKeepsAStoredLargerOneEditable()
	{
		var stored = Store(Widget(0, 0, 2, 1));
		ClaimWithSingleKeyWidgets();

		var grown = await _service.Update(Copy(stored, 0, 0, 3, 1));
		var edited = await _service.Update(Copy(stored, 0, 0, 2, 1, "{\"label\":\"new\"}"));
		var moved = await _service.Update(Copy(stored, 1, 1, 2, 1));

		Assert.Multiple(() =>
		{
			Assert.That(grown.Error, Is.EqualTo(WidgetError.SpanLimitedByDevice));
			Assert.That(edited.Success, Is.True);
			Assert.That(moved.Success, Is.True);
		});
	}

	[Test]
	public async Task UpdatePositions_RefusesAResizePastTheLimit_AndAcceptsShrinkingAStoredLargerOne()
	{
		var single = Store(Widget(0, 0, 1, 1));
		var wide = Store(Widget(0, 1, 3, 1));
		ClaimWithSingleKeyWidgets();

		var grown = await _service.UpdatePositions(_folderId, [new WidgetPlacement(single.Id, 0, 0, 2, 1)]);
		var shrunk = await _service.UpdatePositions(_folderId, [new WidgetPlacement(wide.Id, 0, 1, 2, 1)]);

		Assert.Multiple(() =>
		{
			Assert.That(grown.Error, Is.EqualTo(WidgetError.SpanLimitedByDevice));
			Assert.That(shrunk.Success, Is.True);
			Assert.That(_cache.GetFolderById(_folderId)!.Widgets.Single(w => w.Id == single.Id).Width, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task CreateMany_RefusesAPastedWidgetPastTheLimit()
	{
		ClaimWithSingleKeyWidgets();

		var result = await _service.CreateMany(_folderId, [Widget(0, 0, 1, 1), Widget(1, 0, 1, 2)]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(WidgetError.SpanLimitedByDevice));
			Assert.That(_cache.GetFolderById(_folderId)!.Widgets, Is.Empty);
		});
	}

	[Test]
	public async Task AProfileNoDeviceLimits_KeepsAcceptingAnySpan()
	{
		Claim(FixedGrid(null, null), "Macro Pad");

		var created = await _service.Create(_folderId, Widget(0, 0, 3, 2));

		Assert.That(created.Success, Is.True);
	}

	private void ClaimWithSingleKeyWidgets()
	{
		Claim(FixedGrid(1, 1), "Stream Deck");
	}

	private void Claim(LayoutDescriptor layout, string name)
	{
		_devices.Devices.Add(new DeviceEntity
		{
			Id = Guid.NewGuid(),
			SecretHash = string.Empty,
			Name = name,
			ClientType = DeviceClientType.Provider,
			FormFactor = DeviceFormFactor.Unknown,
			LayoutReference = "com.example.deck::grid",
			LayoutSnapshot = LayoutSnapshotSerializer.Serialize(layout),
			StartupProfileId = _profileId.ToString(),
			CreatedAt = DateTime.UtcNow,
			LastSeenAt = DateTime.UtcNow
		});
	}

	private WidgetEntity Store(WidgetEntity widget)
	{
		widget.FolderId = _folderId;
		_cache.AddWidget(_folderId, widget);
		return widget;
	}

	private WidgetEntity Copy(WidgetEntity source, int x, int y, int width, int height, string? data = null)
	{
		return new WidgetEntity
		{
			Id = source.Id,
			FolderId = _folderId,
			Type = source.Type,
			PositionX = x,
			PositionY = y,
			Width = width,
			Height = height,
			Data = data ?? source.Data
		};
	}

	private static WidgetEntity Widget(int x, int y, int width, int height)
	{
		return new WidgetEntity
		{
			Id = Guid.NewGuid(),
			PositionX = x,
			PositionY = y,
			Width = width,
			Height = height,
			Data = "{}"
		};
	}

	private static LayoutDescriptor FixedGrid(int? maxWidgetColumns, int? maxWidgetRows)
	{
		return new LayoutDescriptor("grid",
			"Deck",
			[
				new LayoutRegion
				{
					Id = "grid",
					Kind = LayoutRegionKinds.Grid,
					Grid = new LayoutGrid
					{
						Rows = 3,
						Columns = 5,
						MaxWidgetColumns = maxWidgetColumns,
						MaxWidgetRows = maxWidgetRows
					}
				}
			]);
	}
}
