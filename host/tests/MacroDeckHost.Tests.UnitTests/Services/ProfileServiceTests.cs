using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class ProfileServiceTests
{
	private InMemoryProfileStore _store = null!;
	private ProfileCache _cache = null!;
	private ProfileService _service = null!;

	[SetUp]
	public async Task SetUp()
	{
		_store = new InMemoryProfileStore();
		_cache = new ProfileCache(_store, new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		var mediator = new RecordingMediator();
		_service = new ProfileService(_cache, new FolderCache(_cache), new InMemoryDeviceRepository(), mediator);
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task Create_SeedsProfileWithOnePersistedStartFolder()
	{
		var result = await _service.Create("Gaming");

		Assert.That(result.Success, Is.True);
		var profileId = result.Data!.Id;
		var folders = _cache.GetFoldersByProfileId(profileId);
		Assert.Multiple(() =>
		{
			Assert.That(folders, Has.Count.EqualTo(1));
			Assert.That(folders.Single().Name, Is.EqualTo("Home"));
			Assert.That(folders.Single().IsDefault, Is.True);
			Assert.That(folders.Single().CreatedAt, Is.Not.EqualTo(default(DateTime)));
			Assert.That(folders.Single().Widgets, Is.Empty);
			Assert.That(_store.Get(profileId)!.Folders.Single().IsDefault, Is.True);
			Assert.That(_store.Get(profileId)!.Folders.Single().CreatedAt, Is.Not.EqualTo(default(DateTime)));
		});
	}

	[Test]
	public async Task Create_AppliesDefaultsToSeededFolder()
	{
		var result = await _service.Create("Big", defaultRows: 6, defaultColumns: 8, defaultBackgroundColor: "#000");

		var folder = _cache.GetFoldersByProfileId(result.Data!.Id).Single();
		Assert.Multiple(() =>
		{
			// Rows/Columns start inherited (issue #246/#32), not copied, so a later profile edit reaches
			// the folder. Background colour is still copied - out of scope for this issue.
			Assert.That(folder.Rows, Is.Null);
			Assert.That(folder.Columns, Is.Null);
			Assert.That(folder.BackgroundColor, Is.EqualTo("#000"));
		});
	}

	[Test]
	public async Task Create_RejectsEmptyName()
	{
		var result = await _service.Create("  ");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(ProfileError.ValidationError));
		});
	}

	[Test]
	public async Task Delete_LastProfile_Fails()
	{
		var created = await _service.Create("Only");

		var result = await _service.Delete(created.Data!.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(ProfileError.CannotDeleteLastProfile));
		});
	}

	[Test]
	public async Task Delete_RemovesProfileWhenOthersRemain()
	{
		var first = await _service.Create("First");
		await _service.Create("Second");

		var result = await _service.Delete(first.Data!.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_cache.GetById(first.Data!.Id), Is.Null);
		});
	}
}
