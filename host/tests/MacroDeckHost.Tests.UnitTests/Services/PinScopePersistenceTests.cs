using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class PinScopePersistenceTests
{
	private PinScopeFixture _fixture = null!;

	[SetUp]
	public void SetUp() => _fixture = new PinScopeFixture();

	[TearDown]
	public void TearDown() => _fixture.Dispose();

	[Test]
	public async Task E1_MigrationGuard_AWidgetWithNoStoredScope_LoadsAsProfileWide()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);
		w.IsPinned = true;
		await _fixture.FolderCache.AddOrUpdate(_fixture.Main);

		var (reloadedCache, reloadedFolderCache, reloadedWidgetService) = await Reload(_fixture.Store);
		try
		{
			var stored = reloadedFolderCache.GetFolderById(_fixture.Main.Id)!.Widgets.Single();
			Assert.That(stored.PinScope, Is.EqualTo(PinScope.Profile));

			var create = await reloadedWidgetService.Create(_fixture.Mail.Id, Widget(0, 0));
			Assert.Multiple(() =>
			{
				Assert.That(create.Success, Is.False);
				Assert.That(create.Error, Is.EqualTo(WidgetError.PositionOccupied));
			});
		}
		finally
		{
			reloadedCache.Dispose();
		}
	}

	[Test]
	public async Task E2_PersistAndReload_RoundTripsBothScopesAndTheirRects()
	{
		var profileWidget = _fixture.AddWidget(_fixture.Main, 0, 0);
		await _fixture.WidgetService.SetPinned(_fixture.Main.Id, profileWidget.Id, true, PinScope.Profile);
		_fixture.AddWidget(_fixture.Mail, 1, 1);
		var subtreeWidget = _fixture.AddWidget(_fixture.Main, 1, 1);
		var granted =
			await _fixture.WidgetService.SetPinned(_fixture.Main.Id, subtreeWidget.Id, true, PinScope.Subtree);
		Assert.That(granted.Success, Is.True);

		var (reloadedCache, reloadedFolderCache, _) = await Reload(_fixture.Store);
		try
		{
			var reloadedMain = reloadedFolderCache.GetFolderById(_fixture.Main.Id)!;
			var reloadedProfileWidget = reloadedMain.Widgets.Single(w => w.Id == profileWidget.Id);
			var reloadedSubtreeWidget = reloadedMain.Widgets.Single(w => w.Id == subtreeWidget.Id);

			Assert.Multiple(() =>
			{
				Assert.That(reloadedProfileWidget.IsPinned, Is.True);
				Assert.That(reloadedProfileWidget.PinScope, Is.EqualTo(PinScope.Profile));
				Assert.That((reloadedProfileWidget.PositionX, reloadedProfileWidget.PositionY), Is.EqualTo((0, 0)));

				Assert.That(reloadedSubtreeWidget.IsPinned, Is.True);
				Assert.That(reloadedSubtreeWidget.PinScope, Is.EqualTo(PinScope.Subtree));
				Assert.That((reloadedSubtreeWidget.PositionX, reloadedSubtreeWidget.PositionY), Is.EqualTo((1, 1)));
			});
		}
		finally
		{
			reloadedCache.Dispose();
		}
	}

	[Test]
	public async Task E3_UnpinnedAfterPersistAndReload_ReservesNothingAnywhere()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);
		await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Subtree);
		var unpinned = await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, false);
		Assert.That(unpinned.Success, Is.True);

		var (reloadedCache, _, reloadedWidgetService) = await Reload(_fixture.Store);
		try
		{
			var create = await reloadedWidgetService.Create(_fixture.Retro.Id, Widget(0, 0));
			Assert.That(create.Success, Is.True);
		}
		finally
		{
			reloadedCache.Dispose();
		}
	}

	private static async Task<(ProfileCache Cache, FolderCache Folders, WidgetService Widgets)> Reload(
		InMemoryProfileStore store)
	{
		var cache = new ProfileCache(store, new LoggerConfiguration().CreateLogger());
		await cache.InitializeCache();
		var folderCache = new FolderCache(cache);
		var secrets = new FakeSecretService();
		var widgetService = new WidgetService(folderCache,
			cache,
			new RecordingMediator(),
			new WidgetSecretScrubber(secrets),
			new WidgetSecretCloner(secrets),
			new NullWidgetVariableCloner());
		return (cache, folderCache, widgetService);
	}

	private static Domain.Entities.WidgetEntity Widget(int x, int y) => new()
	{
		Id = Guid.NewGuid(),
		Type = WidgetTypeIds.ActionButton,
		PositionX = x,
		PositionY = y,
		Width = 1,
		Height = 1
	};
}
