using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class ProfileServiceDuplicateTests
{
	private ProfileCache _cache = null!;
	private FolderCache _folderCache = null!;
	private FakeSecretService _secrets = null!;
	private VariableService _variables = null!;
	private RecordingMediator _mediator = null!;
	private ProfileService _service = null!;

	private Guid _profileId;
	private Guid _otherProfileFolderId;
	private Guid _homeId;
	private Guid _scenesId;
	private Guid _subId;
	private Guid _micWidgetId;
	private Guid _linkWidgetId;
	private Guid _secretId;

	[SetUp]
	public async Task SetUp()
	{
		_cache = new ProfileCache(new InMemoryProfileStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		_folderCache = new FolderCache(_cache);
		_secrets = new FakeSecretService();
		_mediator = new RecordingMediator();
		_variables = TestVariableServices.Create(new VariableRegistry(), new NullUserVariableStore(), _mediator);
		_service = new ProfileService(_cache,
			_folderCache,
			new InMemoryDeviceRepository(),
			_mediator,
			new WidgetSecretCloner(_secrets),
			new WidgetVariableCloner(_variables, new LoggerConfiguration().CreateLogger()));

		var otherProfileId = Guid.NewGuid();
		_otherProfileFolderId = Guid.NewGuid();
		await _cache.AddOrUpdate(new ProfileEntity { Id = otherProfileId, Name = "Work", Order = 0 });
		await _cache.AddOrUpdateFolder(new FolderEntity
		{
			Id = _otherProfileFolderId,
			ProfileId = otherProfileId,
			Name = "Home",
			Order = 0,
			IsDefault = true,
			CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
		});

		_profileId = Guid.NewGuid();
		await _cache.AddOrUpdate(new ProfileEntity
		{
			Id = _profileId,
			Name = "Streaming",
			Order = 1,
			DefaultRows = 3,
			DefaultColumns = 5,
			DefaultBackgroundColor = "#101010",
			DefaultWidgetSpacing = 8,
			DefaultWidgetBorderRadius = 12,
			DefaultWidgetShadows = false
		});

		_homeId = Guid.NewGuid();
		_scenesId = Guid.NewGuid();
		_subId = Guid.NewGuid();
		await _cache.AddOrUpdateFolder(new FolderEntity
		{
			Id = _homeId,
			ProfileId = _profileId,
			Name = "Home",
			Order = 0,
			IsDefault = true,
			CreatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
		});
		await _cache.AddOrUpdateFolder(new FolderEntity
		{
			Id = _scenesId,
			ProfileId = _profileId,
			Name = "Scenes",
			Order = 1,
			Rows = 4,
			Columns = 6,
			BackgroundColor = "#202020",
			WidgetSpacing = 4,
			WidgetBorderRadius = 6,
			FocusRules =
			[
				new FolderFocusRule
				{
					Id = Guid.NewGuid(),
					ApplicationIdentity = "obs",
					IdentityKind = ApplicationIdentityKind.ProcessName,
					DeviceId = Guid.NewGuid()
				}
			],
			CreatedAt = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc)
		});
		await _cache.AddOrUpdateFolder(new FolderEntity
		{
			Id = _subId,
			ProfileId = _profileId,
			ParentId = _scenesId,
			Name = "Sub",
			Order = 0,
			ViewId = "example.integration:board",
			ViewConfiguration = $"{{\"backFolder\":\"{_homeId}\"}}",
			CreatedAt = new DateTime(2026, 2, 3, 0, 0, 0, DateTimeKind.Utc)
		});

		_secretId = _secrets.Store("stream-key");
		_micWidgetId = Guid.NewGuid();
		_linkWidgetId = Guid.NewGuid();
		_cache.AddWidget(_homeId,
			new WidgetEntity
			{
				Id = _micWidgetId,
				FolderId = _homeId,
				Type = WidgetTypeIds.ActionButton,
				PositionX = 1,
				PositionY = 2,
				Width = 2,
				Height = 1,
				Data = $"{{\"$secret\":\"{_secretId}\"}}"
			});
		_cache.AddWidget(_homeId,
			new WidgetEntity
			{
				Id = _linkWidgetId,
				FolderId = _homeId,
				Type = WidgetTypeIds.ActionButton,
				PositionX = 0,
				PositionY = 0,
				Data = $"{{\"openFolder\":\"{_subId}\",\"toggles\":\"{_micWidgetId}\"," +
					$"\"switchProfile\":\"{_profileId}\",\"outside\":\"{_otherProfileFolderId}\"}}"
			});
		_cache.AddWidget(_homeId,
			new WidgetEntity
			{
				Id = Guid.NewGuid(),
				FolderId = _homeId,
				Type = WidgetTypeIds.ActionButton,
				PositionX = 4,
				PositionY = 2,
				IsPinned = true,
				PinScope = PinScope.Profile
			});
		_cache.AddWidget(_scenesId,
			new WidgetEntity
			{
				Id = Guid.NewGuid(),
				FolderId = _scenesId,
				Type = WidgetTypeIds.ActionButton,
				PositionX = 5,
				PositionY = 3,
				IsPinned = true,
				PinScope = PinScope.Subtree
			});
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task Duplicate_CreatesANewProfileWithTheSameSettingsNamedAsACopy()
	{
		var result = await _service.Duplicate(_profileId);

		Assert.That(result.Success, Is.True);
		var copy = result.Data!;
		Assert.Multiple(() =>
		{
			Assert.That(copy.Id, Is.Not.EqualTo(_profileId));
			Assert.That(copy.Name, Is.EqualTo("Streaming (copy)"));
			Assert.That(copy.Order, Is.EqualTo(2));
			Assert.That(copy.DefaultRows, Is.EqualTo(3));
			Assert.That(copy.DefaultColumns, Is.EqualTo(5));
			Assert.That(copy.DefaultBackgroundColor, Is.EqualTo("#101010"));
			Assert.That(copy.DefaultWidgetSpacing, Is.EqualTo(8));
			Assert.That(copy.DefaultWidgetBorderRadius, Is.EqualTo(12));
			Assert.That(copy.DefaultWidgetShadows, Is.False);
			Assert.That(_cache.GetAll().Select(profile => profile.Id), Does.Contain(copy.Id));
		});
	}

	[Test]
	public async Task Duplicate_CopiesTheFolderTreeAndStartFolderButNotAutomaticActivation()
	{
		var copy = (await _service.Duplicate(_profileId)).Data!;

		var folders = _folderCache.GetFoldersByProfileId(copy.Id);
		var home = folders.Single(folder => folder.Name == "Home");
		var scenes = folders.Single(folder => folder.Name == "Scenes");
		var sub = folders.Single(folder => folder.Name == "Sub");
		Assert.Multiple(() =>
		{
			Assert.That(folders, Has.Count.EqualTo(3));
			Assert.That(folders.Select(folder => folder.Id), Has.None.AnyOf(_homeId, _scenesId, _subId));
			Assert.That(home.IsDefault, Is.True);
			Assert.That(scenes.IsDefault, Is.False);
			Assert.That(home.ParentId, Is.Null);
			Assert.That(scenes.ParentId, Is.Null);
			Assert.That(sub.ParentId, Is.EqualTo(scenes.Id));
			Assert.That(scenes.Rows, Is.EqualTo(4));
			Assert.That(scenes.Columns, Is.EqualTo(6));
			Assert.That(scenes.BackgroundColor, Is.EqualTo("#202020"));
			Assert.That(scenes.WidgetSpacing, Is.EqualTo(4));
			Assert.That(scenes.WidgetBorderRadius, Is.EqualTo(6));
			Assert.That(sub.ViewId, Is.EqualTo("example.integration:board"));
			Assert.That(scenes.FocusRules, Is.Empty);
		});
	}

	[Test]
	public async Task Duplicate_CopiesEveryWidgetIncludingPinnedOnesIntoTheMatchingFolder()
	{
		var copy = (await _service.Duplicate(_profileId)).Data!;

		var folders = _folderCache.GetFoldersByProfileId(copy.Id);
		var home = folders.Single(folder => folder.Name == "Home");
		var scenes = folders.Single(folder => folder.Name == "Scenes");
		var mic = home.Widgets.Single(widget => widget.PositionX == 1);
		Assert.Multiple(() =>
		{
			Assert.That(home.Widgets, Has.Count.EqualTo(3));
			Assert.That(home.Widgets.All(widget => widget.FolderId == home.Id), Is.True);
			Assert.That(home.Widgets.Count(widget => widget is { IsPinned: true, PinScope: PinScope.Profile }),
				Is.EqualTo(1));
			Assert.That(scenes.Widgets.Single().PinScope, Is.EqualTo(PinScope.Subtree));
			Assert.That(scenes.Widgets.Single().IsPinned, Is.True);
			Assert.That((mic.PositionY, mic.Width, mic.Height), Is.EqualTo((2, 2, 1)));
			Assert.That(mic.Id, Is.Not.EqualTo(_micWidgetId));
		});
	}

	[Test]
	public async Task Duplicate_PointsReferencesToTheProfilesOwnContentAtTheCopy()
	{
		var copy = (await _service.Duplicate(_profileId)).Data!;

		var folders = _folderCache.GetFoldersByProfileId(copy.Id);
		var home = folders.Single(folder => folder.Name == "Home");
		var sub = folders.Single(folder => folder.Name == "Sub");
		var mic = home.Widgets.Single(widget => widget.PositionX == 1);
		var link = home.Widgets.Single(widget => widget.PositionX == 0).Data!;
		Assert.Multiple(() =>
		{
			Assert.That(link, Does.Contain($"\"openFolder\":\"{sub.Id}\""));
			Assert.That(link, Does.Contain($"\"toggles\":\"{mic.Id}\""));
			Assert.That(link, Does.Contain($"\"switchProfile\":\"{copy.Id}\""));
			Assert.That(link, Does.Contain($"\"outside\":\"{_otherProfileFolderId}\""));
			Assert.That(sub.ViewConfiguration, Is.EqualTo($"{{\"backFolder\":\"{home.Id}\"}}"));
		});
	}

	[Test]
	public async Task Duplicate_GivesTheCopyItsOwnSecretsAndWidgetVariables()
	{
		await _variables.CreateUserVariable("presses",
			VariableScope.Widget,
			_micWidgetId.ToString(),
			VariableType.Numeric,
			"7",
			0);

		var copy = (await _service.Duplicate(_profileId)).Data!;

		var mic = _folderCache.GetFoldersByProfileId(copy.Id)
			.Single(folder => folder.Name == "Home")
			.Widgets.Single(widget => widget.PositionX == 1);
		var copiedSecret = Guid.Parse(mic.Data!.Split('"')[3]);
		var variables = await _variables.GetByScope(VariableScope.Widget, mic.Id.ToString());
		Assert.Multiple(() =>
		{
			Assert.That(copiedSecret, Is.Not.EqualTo(_secretId));
			Assert.That(_secrets.Resolve(copiedSecret).Result, Is.EqualTo("stream-key"));
			Assert.That(variables.Single().Name, Is.EqualTo("presses"));
			Assert.That(variables.Single().Value, Is.EqualTo("7"));
		});
	}

	[Test]
	public async Task Duplicate_LeavesTheOriginalProfileUntouched()
	{
		var before = _folderCache.GetFoldersByProfileId(_profileId)
			.SelectMany(folder => folder.Widgets.Select(widget => (folder.Id, widget.Id, widget.Data)))
			.OrderBy(entry => entry.Item2)
			.ToList();

		var copy = (await _service.Duplicate(_profileId)).Data!;
		_folderCache.GetFoldersByProfileId(copy.Id).Single(folder => folder.Name == "Home").Name = "Renamed";

		var original = _folderCache.GetFoldersByProfileId(_profileId);
		var after = original
			.SelectMany(folder => folder.Widgets.Select(widget => (folder.Id, widget.Id, widget.Data)))
			.OrderBy(entry => entry.Item2)
			.ToList();
		Assert.Multiple(() =>
		{
			Assert.That(_cache.GetById(_profileId)!.Name, Is.EqualTo("Streaming"));
			Assert.That(original.Select(folder => folder.Name), Is.EquivalentTo(new[] { "Home", "Scenes", "Sub" }));
			Assert.That(original.Single(folder => folder.Id == _scenesId).FocusRules, Has.Count.EqualTo(1));
			Assert.That(after, Is.EqualTo(before));
		});
	}

	[Test]
	public async Task Duplicate_UsesTheRequestedNameAndNumbersNamesAlreadyTaken()
	{
		var first = await _service.Duplicate(_profileId, "  Streaming B  ");
		var second = await _service.Duplicate(_profileId, "Streaming B");
		var defaulted = await _service.Duplicate(_profileId, "   ");
		var defaultedAgain = await _service.Duplicate(_profileId);

		Assert.Multiple(() =>
		{
			Assert.That(first.Data!.Name, Is.EqualTo("Streaming B"));
			Assert.That(second.Data!.Name, Is.EqualTo("Streaming B 2"));
			Assert.That(defaulted.Data!.Name, Is.EqualTo("Streaming (copy)"));
			Assert.That(defaultedAgain.Data!.Name, Is.EqualTo("Streaming (copy) 2"));
		});
	}

	[Test]
	public async Task Duplicate_AnnouncesTheNewProfile()
	{
		var copy = (await _service.Duplicate(_profileId)).Data!;

		Assert.That(_mediator.Published.OfType<ProfileCreatedNotification>().Select(n => n.Profile.Id),
			Does.Contain(copy.Id));
	}

	[Test]
	public async Task Duplicate_OfAnUnknownProfileFails()
	{
		var result = await _service.Duplicate(Guid.NewGuid());

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(ProfileError.NotFound));
			Assert.That(_cache.GetAll(), Has.Count.EqualTo(2));
		});
	}

	private sealed class NullUserVariableStore : IUserVariableStore
	{
		public IReadOnlyList<VariableEntity> Load() => [];

		public void Save(IEnumerable<VariableEntity> userVariables)
		{
		}
	}
}
