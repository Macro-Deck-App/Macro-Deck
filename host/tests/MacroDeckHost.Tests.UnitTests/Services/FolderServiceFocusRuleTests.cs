using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class FolderServiceFocusRuleTests
{
	private InMemoryProfileStore _store = null!;
	private ProfileCache _cache = null!;
	private FolderService _service = null!;
	private Guid _profileId;
	private Guid _folderAId;
	private Guid _folderBId;

	[SetUp]
	public async Task SetUp()
	{
		_store = new InMemoryProfileStore();
		_cache = new ProfileCache(_store, new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		var secrets = new FakeSecretService();
		_service = new FolderService(new FolderCache(_cache),
			_cache,
			new InMemoryDeviceRepository(),
			new RecordingMediator(),
			new WidgetSecretCloner(secrets),
			new WidgetSecretScrubber(secrets),
			new NullWidgetVariableCloner(),
			TestFolderViewProviders.Registry());

		_profileId = Guid.NewGuid();
		await _cache.AddOrUpdate(new ProfileEntity { Id = _profileId, Name = "P" });

		_folderAId = Guid.NewGuid();
		await _cache.AddOrUpdateFolder(new FolderEntity
		{
			Id = _folderAId, ProfileId = _profileId, Name = "Folder A", Order = 0, Rows = 4, Columns = 4
		});

		_folderBId = Guid.NewGuid();
		await _cache.AddOrUpdateFolder(new FolderEntity
		{
			Id = _folderBId, ProfileId = _profileId, Name = "Folder B", Order = 1, Rows = 4, Columns = 4
		});
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task SetFocusRule_NewRule_AssignsIdAndPersists()
	{
		var deviceId = Guid.NewGuid();

		var result = await _service.SetFocusRule(_folderAId, NewRule(deviceId: deviceId, returnOnFocusLoss: true));

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Id, Is.Not.EqualTo(Guid.Empty));
			Assert.That(result.Data.DeviceId, Is.EqualTo(deviceId));
			Assert.That(result.Data.ReturnOnFocusLoss, Is.True);
		});

		var persistedFolder = _store.Get(_profileId)!.Folders.Single(f => f.Id == _folderAId);
		Assert.That(persistedFolder.FocusRules, Is.Not.Null);
		Assert.That(persistedFolder.FocusRules!.Single().Id, Is.EqualTo(result.Data!.Id));
	}

	[Test]
	public async Task SetFocusRule_ExistingRuleId_EditsInPlaceRatherThanAdding()
	{
		var created = await _service.SetFocusRule(_folderAId, NewRule());
		var ruleId = created.Data!.Id;

		var edited = await _service.SetFocusRule(_folderAId, NewRule(id: ruleId, identity: "wordpad", enabled: false));

		Assert.That(edited.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(edited.Data!.Id, Is.EqualTo(ruleId));
			Assert.That(edited.Data.ApplicationIdentity, Is.EqualTo("wordpad"));
			Assert.That(edited.Data.Enabled, Is.False);
		});

		var folder = _cache.GetFolderById(_folderAId)!;
		Assert.That(folder.FocusRules, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task SetFocusRule_NormalizesTheStoredIdentity()
	{
		var result = await _service.SetFocusRule(_folderAId, NewRule(identity: "  notepad  "));

		Assert.That(result.Data!.ApplicationIdentity, Is.EqualTo("notepad"));
	}

	[Test]
	public async Task SetFocusRule_EmptyIdentity_ReturnsValidationError()
	{
		var result = await _service.SetFocusRule(_folderAId, NewRule(identity: "   "));

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.ValidationError));
		});
	}

	[Test]
	public async Task SetFocusRule_UnknownFolder_ReturnsNotFound()
	{
		var result = await _service.SetFocusRule(Guid.NewGuid(), NewRule());

		Assert.That(result.Error, Is.EqualTo(FolderError.NotFound));
	}

	[Test]
	public async Task SetFocusRule_DuplicateEnabledPair_AcrossFolders_IsRejectedWithTheConflictingFolderName()
	{
		var deviceId = Guid.NewGuid();
		await _service.SetFocusRule(_folderAId, NewRule(deviceId: deviceId, identity: "notepad"));

		var result = await _service.SetFocusRule(_folderBId, NewRule(deviceId: deviceId, identity: "NOTEPAD"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.DuplicateFocusRule));
			Assert.That(result.ErrorMessage, Does.Contain("Folder A"));
		});
	}

	[Test]
	public async Task SetFocusRule_DuplicateEnabledPair_WithinOneFolder_IsRejected()
	{
		var deviceId = Guid.NewGuid();
		await _service.SetFocusRule(_folderAId, NewRule(deviceId: deviceId, identity: "notepad"));

		var result = await _service.SetFocusRule(_folderAId, NewRule(deviceId: deviceId, identity: "notepad"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.DuplicateFocusRule));
		});
	}

	[Test]
	public async Task SetFocusRule_EditingTheSameRule_DoesNotConflictWithItself()
	{
		var deviceId = Guid.NewGuid();
		var created = await _service.SetFocusRule(_folderAId, NewRule(deviceId: deviceId, identity: "notepad"));

		var edited = await _service.SetFocusRule(_folderAId,
			NewRule(id: created.Data!.Id, deviceId: deviceId, identity: "notepad", returnOnFocusLoss: true));

		Assert.That(edited.Success, Is.True);
	}

	[Test]
	public async Task SetFocusRule_DisabledDuplicate_IsAllowed()
	{
		var deviceId = Guid.NewGuid();
		await _service.SetFocusRule(_folderAId, NewRule(deviceId: deviceId, identity: "notepad", enabled: false));

		var result = await _service.SetFocusRule(_folderBId,
			NewRule(deviceId: deviceId, identity: "notepad", enabled: false));

		Assert.That(result.Success, Is.True);
	}

	[Test]
	public async Task DeleteFocusRule_RemovesTheRuleAndPersists()
	{
		var created = await _service.SetFocusRule(_folderAId, NewRule());

		var result = await _service.DeleteFocusRule(_folderAId, created.Data!.Id);

		Assert.That(result.Success, Is.True);
		Assert.That(_cache.GetFolderById(_folderAId)!.FocusRules, Is.Empty);
		var persistedFolder = _store.Get(_profileId)!.Folders.Single(f => f.Id == _folderAId);
		Assert.That(persistedFolder.FocusRules, Is.Null);
	}

	[Test]
	public async Task DeleteFocusRule_UnknownFolder_ReturnsNotFound()
	{
		var result = await _service.DeleteFocusRule(Guid.NewGuid(), Guid.NewGuid());

		Assert.That(result.Error, Is.EqualTo(FolderError.NotFound));
	}

	[Test]
	public async Task DeleteFocusRule_UnknownRuleId_ReturnsNotFound()
	{
		var result = await _service.DeleteFocusRule(_folderAId, Guid.NewGuid());

		Assert.That(result.Error, Is.EqualTo(FolderError.NotFound));
	}

	private static FolderFocusRule NewRule(
		Guid? id = null,
		bool enabled = true,
		string identity = "notepad",
		ApplicationIdentityKind kind = ApplicationIdentityKind.ProcessName,
		Guid? deviceId = null,
		bool returnOnFocusLoss = false)
		=> new()
		{
			Id = id ?? Guid.Empty,
			Enabled = enabled,
			ApplicationIdentity = identity,
			IdentityKind = kind,
			DeviceId = deviceId ?? Guid.NewGuid(),
			ReturnOnFocusLoss = returnOnFocusLoss
		};
}
