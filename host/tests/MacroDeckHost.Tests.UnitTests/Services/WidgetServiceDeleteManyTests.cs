using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class WidgetServiceDeleteManyTests
{
	private ProfileCache _cache = null!;
	private RecordingMediator _mediator = null!;
	private FakeSecretService _secrets = null!;
	private WidgetService _service = null!;
	private Guid _profileId;
	private Guid _folderAId;
	private Guid _folderBId;

	[SetUp]
	public async Task SetUp()
	{
		_cache = new ProfileCache(new InMemoryProfileStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		_mediator = new RecordingMediator();
		_secrets = new FakeSecretService();
		_service = new WidgetService(new FolderCache(_cache),
			_cache,
			_mediator,
			new WidgetSecretScrubber(_secrets),
			new WidgetSecretCloner(_secrets),
			new NullWidgetVariableCloner());

		_profileId = Guid.NewGuid();
		_folderAId = Guid.NewGuid();
		_folderBId = Guid.NewGuid();
		await _cache.AddOrUpdate(new ProfileEntity { Id = _profileId, Name = "P" });
		await _cache.AddOrUpdateFolder(Folder(_folderAId, "A"));
		await _cache.AddOrUpdateFolder(Folder(_folderBId, "B"));
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task DeletesExactlyTheNamedWidgets_SurvivingWidgetsKeepTheirRects()
	{
		var w0 = Widget(0, 0);
		var w1 = Widget(1, 0);
		var w2 = Widget(2, 0);
		var w3 = Widget(3, 0);
		_cache.AddWidget(_folderAId, w0);
		_cache.AddWidget(_folderAId, w1);
		_cache.AddWidget(_folderAId, w2);
		_cache.AddWidget(_folderAId, w3);
		_mediator.Published.Clear();

		var result = await _service.DeleteMany(_folderAId, [w0.Id, w2.Id]);

		Assert.That(result.Success, Is.True);
		var remaining = _cache.GetFolderById(_folderAId)!.Widgets;
		Assert.Multiple(() =>
		{
			Assert.That(remaining.Select(w => w.Id), Is.EquivalentTo(new[] { w1.Id, w3.Id }));
			Assert.That(remaining.Single(w => w.Id == w1.Id).PositionX, Is.EqualTo(1));
			Assert.That(remaining.Single(w => w.Id == w3.Id).PositionX, Is.EqualTo(3));
		});
	}

	[Test]
	public async Task AnUnknownId_FailsWithNotFound_AndTheValidMemberStillExists()
	{
		var kept = Widget(0, 0);
		_cache.AddWidget(_folderAId, kept);
		_mediator.Published.Clear();

		var result = await _service.DeleteMany(_folderAId, [kept.Id, Guid.NewGuid()]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.NotFound));
			Assert.That(_cache.GetFolderById(_folderAId)!.Widgets.Select(w => w.Id), Is.EqualTo(new[] { kept.Id }));
			Assert.That(_mediator.Published, Is.Empty);
		});
	}

	[Test]
	public async Task AnIdBelongingToADifferentFolder_FailsTheWholeRequest_AndThatWidgetStillExists()
	{
		var inA = Widget(0, 0);
		var inB = Widget(0, 0);
		_cache.AddWidget(_folderAId, inA);
		_cache.AddWidget(_folderBId, inB);
		_mediator.Published.Clear();

		var result = await _service.DeleteMany(_folderAId, [inA.Id, inB.Id]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.NotFound));
			Assert.That(_cache.GetFolderById(_folderAId)!.Widgets.Select(w => w.Id), Is.EqualTo(new[] { inA.Id }));
			Assert.That(_cache.GetFolderById(_folderBId)!.Widgets.Select(w => w.Id), Is.EqualTo(new[] { inB.Id }));
			Assert.That(_mediator.Published, Is.Empty);
		});
	}

	[Test]
	public async Task SecretsReferencedByEveryDeletedWidget_AreScrubbed()
	{
		var secretA = _secrets.Store("a");
		var secretB = _secrets.Store("b");
		var w0 = Widget(0, 0);
		w0.Data = $"{{\"$secret\":\"{secretA}\"}}";
		var w1 = Widget(1, 0);
		w1.Data = $"{{\"$secret\":\"{secretB}\"}}";
		_cache.AddWidget(_folderAId, w0);
		_cache.AddWidget(_folderAId, w1);

		var result = await _service.DeleteMany(_folderAId, [w0.Id, w1.Id]);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(_secrets.Resolve(secretA).Result, Is.Null);
			Assert.That(_secrets.Resolve(secretB).Result, Is.Null);
		});
	}

	private FolderEntity Folder(Guid id, string name) => new()
	{
		Id = id,
		ProfileId = _profileId,
		Name = name,
		Order = 0,
		Rows = 4,
		Columns = 4
	};

	private static WidgetEntity Widget(int x, int y) => new()
	{
		Id = Guid.NewGuid(),
		Type = WidgetTypeIds.ActionButton,
		PositionX = x,
		PositionY = y,
		Width = 1,
		Height = 1
	};
}
