using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.FolderViews;

/// <summary>
/// What a folder does with its view (issue #785): choosing one, switching it, and - the case the
/// placeholder exists for - continuing to work while nothing provides it.
/// </summary>
[TestFixture]
public class FolderServiceFolderViewTests
{
	private ProfileCache _cache = null!;
	private FolderCache _folders = null!;
	private FolderService _service = null!;
	private IFolderViewRegistry _folderViews = null!;
	private string _folderViewId = null!;
	private Guid _profileId;

	[SetUp]
	public async Task SetUp()
	{
		_cache = new ProfileCache(new InMemoryProfileStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();

		(_folderViews, _folderViewId) = TestFolderViewProviders.WithView();

		var secrets = new FakeSecretService();
		_folders = new FolderCache(_cache);
		_service = new FolderService(_folders,
			_cache,
			new InMemoryDeviceRepository(),
			new RecordingMediator(),
			new WidgetSecretCloner(secrets),
			new WidgetSecretScrubber(secrets),
			new NullWidgetVariableCloner(),
			_folderViews);

		_profileId = Guid.NewGuid();
		await _cache.AddOrUpdate(new ProfileEntity { Id = _profileId, Name = "P" });
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task AFolderCreatedWithoutAView_IsTheWidgetGrid()
	{
		var created = await _service.Create(_profileId, "Deck", parentId: null);

		Assert.That(created.Success, Is.True);
		Assert.That(BuiltInFolderViews.IsWidgetGrid(created.Data!.ViewId), Is.True);
	}

	[Test]
	public async Task AFolderCanBeCreatedWithAProvidersView()
	{
		var created = await _service.Create(_profileId,
			"Living Room",
			parentId: null,
			_folderViewId,
			"""{"areaId":"living-room"}""");

		Assert.That(created.Success, Is.True);
		Assert.That(created.Data!.ViewId, Is.EqualTo(_folderViewId));
		Assert.That(created.Data.ViewConfiguration, Is.EqualTo("""{"areaId":"living-room"}"""));
	}

	[Test]
	public async Task CreatingWithAViewNothingProvides_IsRejected()
	{
		var created = await _service.Create(_profileId, "Living Room", parentId: null, "com.example.gone::view");

		Assert.That(created.Success, Is.False);
		Assert.That(created.Error, Is.EqualTo(FolderError.UnknownFolderView));
	}

	[Test]
	public async Task SwitchingBackToTheGrid_DropsTheProvidersConfiguration()
	{
		var folder = await CreateWithView();

		var updated = await Update(folder.Id, folderViewId: BuiltInFolderViews.WidgetGrid);

		Assert.That(updated.Success, Is.True);
		Assert.That(BuiltInFolderViews.IsWidgetGrid(updated.Data!.ViewId), Is.True);
		Assert.That(updated.Data.ViewConfiguration, Is.Null);
	}

	/// <summary>A configuration belongs to the view that wrote it - handing it to the next provider would
	/// be handing it a shape it has never seen.</summary>
	[Test]
	public async Task SwitchingToAnotherView_DropsTheConfigurationTheOldViewWrote()
	{
		var folder = await CreateWithView();
		var second = await _folderViews.Register("com.example.obs", Descriptor("mixer"));

		var updated = await Update(folder.Id, folderViewId: second.FolderViewId);

		Assert.That(updated.Success, Is.True);
		Assert.That(updated.Data!.ViewId, Is.EqualTo(second.FolderViewId));
		Assert.That(updated.Data.ViewConfiguration, Is.Null);
	}

	[Test]
	public async Task AConfigurationCanBeChangedWithoutSwitchingViews()
	{
		var folder = await CreateWithView();

		var updated = await Update(folder.Id, folderViewConfiguration: """{"areaId":"kitchen"}""");

		Assert.That(updated.Success, Is.True);
		Assert.That(updated.Data!.ViewId, Is.EqualTo(_folderViewId));
		Assert.That(updated.Data.ViewConfiguration, Is.EqualTo("""{"areaId":"kitchen"}"""));
	}

	/// <summary>
	/// The regression the whole "unavailable provider" story rests on: a folder keeps its view id while
	/// its integration is away, and renaming or re-parenting it must not be refused because of it.
	/// </summary>
	[Test]
	public async Task AFolderWhoseProviderIsGone_StillAcceptsAnUnrelatedEdit()
	{
		var folder = await CreateWithView();
		await _folderViews.UnregisterAll("com.example.home");

		var renamed = await Update(folder.Id, name: "Renamed");

		Assert.That(renamed.Success, Is.True);
		Assert.That(renamed.Data!.Name, Is.EqualTo("Renamed"));
		Assert.That(renamed.Data.ViewId, Is.EqualTo(_folderViewId), "the stored view id must survive");
		Assert.That(renamed.Data.ViewConfiguration, Is.EqualTo("""{"areaId":"living-room"}"""));
	}

	[Test]
	public async Task AFolderWhoseProviderIsGone_CannotHaveThatViewsConfigurationChanged()
	{
		var folder = await CreateWithView();
		await _folderViews.UnregisterAll("com.example.home");

		var updated = await Update(folder.Id, folderViewConfiguration: """{"areaId":"kitchen"}""");

		Assert.That(updated.Success, Is.False);
		Assert.That(updated.Error, Is.EqualTo(FolderError.UnknownFolderView));
	}

	[Test]
	public async Task ADuplicatedFolder_KeepsTheViewEvenWhileItsProviderIsGone()
	{
		var folder = await CreateWithView();
		await _folderViews.UnregisterAll("com.example.home");

		var duplicated = await _service.Duplicate(folder.Id);

		Assert.That(duplicated.Success, Is.True);
		Assert.That(duplicated.Data!.ViewId, Is.EqualTo(_folderViewId));
		Assert.That(duplicated.Data.ViewConfiguration, Is.EqualTo("""{"areaId":"living-room"}"""));
	}

	/// <summary>A profile written before folder views existed carries no id at all, and must load as a grid
	/// rather than as a folder pointing at nothing.</summary>
	[Test]
	public async Task AFolderStoredWithoutAView_RoundTripsAsTheWidgetGrid()
	{
		var created = await _service.Create(_profileId, "Deck", parentId: null);

		var file = ProfileFileMapper.ToFolderFile(created.Data!);
		Assert.That(file.ViewId, Is.Null, "an absent view must not be written out as a literal id");

		var reloaded = ProfileFileMapper.ToFolderEntities(new Application.Persistence.Profiles.ProfileFile
		{
			Id = _profileId, Folders = [file]
		});

		Assert.That(BuiltInFolderViews.IsWidgetGrid(reloaded.Single().ViewId), Is.True);
	}

	[Test]
	public async Task AFolderWithAView_RoundTripsThroughTheProfileFile()
	{
		var folder = await CreateWithView();

		var reloaded = ProfileFileMapper.ToFolderEntities(new Application.Persistence.Profiles.ProfileFile
		{
			Id = _profileId, Folders = [ProfileFileMapper.ToFolderFile(folder)]
		}).Single();

		Assert.That(reloaded.ViewId, Is.EqualTo(_folderViewId));
		Assert.That(reloaded.ViewConfiguration, Is.EqualTo("""{"areaId":"living-room"}"""));
	}

	private async Task<FolderEntity> CreateWithView()
		=> (await _service.Create(_profileId,
			"Living Room",
			parentId: null,
			_folderViewId,
			"""{"areaId":"living-room"}""")).Data!;

	private Task<Domain.Common.Result<FolderEntity, FolderError>> Update(
		Guid id,
		string? name = null,
		string? folderViewId = null,
		string? folderViewConfiguration = null)
		=> _service.Update(id,
			name,
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			folderViewId,
			folderViewConfiguration);

	private static MacroDeck.Sdk.FolderViews.FolderViewDescriptor Descriptor(string id)
		=> new(id, MacroDeck.Localization.LocalizedText.FromLiteral("Mixer"));
}
