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
public class EmptyCellStyleServiceTests
{
	private ProfileCache _cache = null!;
	private FolderService _folderService = null!;
	private ProfileService _profileService = null!;
	private Guid _profileId;
	private Guid _folderId;

	[SetUp]
	public async Task SetUp()
	{
		_cache = new ProfileCache(new InMemoryProfileStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		var secrets = new FakeSecretService();
		var folderCache = new FolderCache(_cache);
		_folderService = new FolderService(folderCache,
			_cache,
			new InMemoryDeviceRepository(),
			new RecordingMediator(),
			new WidgetSecretCloner(secrets),
			new WidgetSecretScrubber(secrets),
			new NullWidgetVariableCloner(),
			TestFolderViewProviders.Registry());
		_profileService = new ProfileService(_cache,
			folderCache,
			new InMemoryDeviceRepository(),
			new RecordingMediator(),
			new WidgetSecretCloner(secrets),
			new NullWidgetVariableCloner());

		_profileId = (await _profileService.Create("Deck")).Data!.Id;
		_folderId = _cache.GetFoldersByProfileId(_profileId).Single().Id;
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task A_new_profile_and_its_start_folder_leave_empty_cells_unset()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_cache.GetById(_profileId)!.DefaultEmptyCellStyle, Is.Null);
			Assert.That(_cache.GetFolderById(_folderId)!.EmptyCellStyle, Is.Null);
		});
	}

	[TestCase("transparent", EmptyCellStyle.Transparent)]
	[TestCase("Visible", EmptyCellStyle.Visible)]
	public async Task Folder_update_sets_the_empty_cell_style(string value, EmptyCellStyle expected)
	{
		var result = await UpdateFolder(value);

		Assert.That(result.Success, Is.True);
		Assert.That(_cache.GetFolderById(_folderId)!.EmptyCellStyle, Is.EqualTo(expected));
	}

	[Test]
	public async Task Folder_update_with_an_empty_string_clears_back_to_inherit()
	{
		await UpdateFolder("transparent");

		var result = await UpdateFolder("");

		Assert.That(result.Success, Is.True);
		Assert.That(_cache.GetFolderById(_folderId)!.EmptyCellStyle, Is.Null);
	}

	[Test]
	public async Task Folder_update_without_the_value_leaves_it_unchanged()
	{
		await UpdateFolder("transparent");

		var result = await _folderService.Update(_folderId, "Renamed", null, null, null, null, null, null, null, null);

		Assert.That(result.Success, Is.True);
		Assert.That(_cache.GetFolderById(_folderId)!.EmptyCellStyle, Is.EqualTo(EmptyCellStyle.Transparent));
	}

	[Test]
	public async Task Folder_update_rejects_an_unknown_value_and_applies_nothing()
	{
		await UpdateFolder("transparent");

		var result = await _folderService.Update(_folderId,
			"Renamed",
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			emptyCellStyle: "hidden");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.ValidationError));
			Assert.That(_cache.GetFolderById(_folderId)!.Name, Is.EqualTo("Home"));
			Assert.That(_cache.GetFolderById(_folderId)!.EmptyCellStyle, Is.EqualTo(EmptyCellStyle.Transparent));
		});
	}

	[Test]
	public async Task Duplicating_a_folder_copies_its_empty_cell_style()
	{
		await UpdateFolder("transparent");

		var result = await _folderService.Duplicate(_folderId);

		Assert.That(result.Data!.EmptyCellStyle, Is.EqualTo(EmptyCellStyle.Transparent));
	}

	[Test]
	public async Task Profile_update_sets_clears_and_keeps_the_default()
	{
		var setValue = (await UpdateProfile("transparent")).Data!.DefaultEmptyCellStyle;
		var keptValue = (await UpdateProfile(null)).Data!.DefaultEmptyCellStyle;
		var clearedValue = (await UpdateProfile("")).Data!.DefaultEmptyCellStyle;

		Assert.Multiple(() =>
		{
			Assert.That(setValue, Is.EqualTo(EmptyCellStyle.Transparent));
			Assert.That(keptValue, Is.EqualTo(EmptyCellStyle.Transparent));
			Assert.That(clearedValue, Is.Null);
		});
	}

	[Test]
	public async Task Profile_update_rejects_an_unknown_value()
	{
		var result = await UpdateProfile("invisible");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(ProfileError.ValidationError));
			Assert.That(_cache.GetById(_profileId)!.DefaultEmptyCellStyle, Is.Null);
		});
	}

	[Test]
	public async Task Profile_create_accepts_a_default_and_rejects_an_unknown_one()
	{
		var created = await _profileService.Create("Stream", defaultEmptyCellStyle: "transparent");
		var rejected = await _profileService.Create("Broken", defaultEmptyCellStyle: "none");

		Assert.Multiple(() =>
		{
			Assert.That(created.Data!.DefaultEmptyCellStyle, Is.EqualTo(EmptyCellStyle.Transparent));
			Assert.That(rejected.Success, Is.False);
			Assert.That(rejected.Error, Is.EqualTo(ProfileError.ValidationError));
		});
	}

	[Test]
	public async Task Duplicating_a_profile_copies_the_default_and_every_folder_override()
	{
		await UpdateProfile("transparent");
		await UpdateFolder("visible");

		var copy = await _profileService.Duplicate(_profileId);

		var copiedFolder = _cache.GetFoldersByProfileId(copy.Data!.Id).Single();
		Assert.Multiple(() =>
		{
			Assert.That(copy.Data!.DefaultEmptyCellStyle, Is.EqualTo(EmptyCellStyle.Transparent));
			Assert.That(copiedFolder.EmptyCellStyle, Is.EqualTo(EmptyCellStyle.Visible));
		});
	}

	private Task<Result<FolderEntity, FolderError>> UpdateFolder(string value)
		=> _folderService.Update(_folderId, null, null, null, null, null, null, null, null, null, emptyCellStyle: value);

	private Task<Result<ProfileEntity, ProfileError>> UpdateProfile(string? value)
		=> _profileService.Update(_profileId, null, null, null, null, null, null, null, value);
}
