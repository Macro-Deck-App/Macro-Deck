using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class MoveFolderRequestMessageHandlerTests
{
	private ProfileCache _cache = null!;
	private MoveFolderRequestMessageHandler _handler = null!;
	private Guid _profileId;
	private FolderEntity _root = null!;
	private FolderEntity _sibling = null!;
	private FolderEntity _child = null!;

	[SetUp]
	public async Task SetUp()
	{
		_cache = new ProfileCache(new InMemoryProfileStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		var secrets = new FakeSecretService();
		var service = new FolderService(new FolderCache(_cache),
			_cache,
			new InMemoryDeviceRepository(),
			new RecordingMediator(),
			new WidgetSecretCloner(secrets),
			new WidgetSecretScrubber(secrets),
			new NullWidgetVariableCloner(),
			TestFolderViewProviders.Registry());
		_handler = new MoveFolderRequestMessageHandler(service);

		_profileId = Guid.NewGuid();
		await _cache.AddOrUpdate(new ProfileEntity { Id = _profileId, Name = "P" });

		_root = await AddFolder("Root", null, 0, isDefault: true);
		_sibling = await AddFolder("Sibling", null, 1);
		_child = await AddFolder("Child", _root.Id, 0);
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task InvalidFolderGuid_ReturnsValidationError()
	{
		var response = await _handler.Handle(new MoveFolderRequest
			{
				Id = "not-a-guid",
				TargetId = _sibling.Id.ToString(),
				Position = "after"
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(FolderError.ValidationError)));
		});
	}

	[Test]
	public async Task InvalidTargetGuid_ReturnsValidationError()
	{
		var response = await _handler.Handle(new MoveFolderRequest
			{
				Id = _root.Id.ToString(),
				TargetId = "not-a-guid",
				Position = "after"
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(FolderError.ValidationError)));
		});
	}

	[Test]
	public async Task UnknownPosition_ReturnsValidationError()
	{
		var response = await _handler.Handle(new MoveFolderRequest
			{
				Id = _sibling.Id.ToString(),
				TargetId = _root.Id.ToString(),
				Position = "sideways"
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(FolderError.ValidationError)));
		});
	}

	[TestCase("before")]
	[TestCase("BEFORE")]
	[TestCase("Before")]
	[TestCase("inSIDE")]
	public async Task PositionParsing_IsCaseInsensitive(string position)
	{
		var response = await _handler.Handle(new MoveFolderRequest
			{
				Id = _sibling.Id.ToString(),
				TargetId = _root.Id.ToString(),
				Position = position
			},
			CancellationToken.None);

		Assert.That(response.Success, Is.True);
	}

	[Test]
	public async Task ServiceError_IsMappedToTransportError()
	{
		var response = await _handler.Handle(new MoveFolderRequest
			{
				Id = _root.Id.ToString(),
				TargetId = _root.Id.ToString(),
				Position = "after"
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(FolderError.InvalidParent)));
		});
	}

	[Test]
	public async Task ValidRequest_MapsPlacementsWithNullParentIdForRoots()
	{
		var response = await _handler.Handle(new MoveFolderRequest
			{
				Id = _sibling.Id.ToString(),
				TargetId = _root.Id.ToString(),
				Position = "before"
			},
			CancellationToken.None);

		Assert.That(response.Success, Is.True);
		var placements = response.Folders!;
		Assert.Multiple(() =>
		{
			Assert.That(placements.Select(p => p.Id), Does.Contain(_sibling.Id.ToString()));
			var siblingPlacement = placements.Single(p => p.Id == _sibling.Id.ToString());
			Assert.That(siblingPlacement.ParentId, Is.Null);
			Assert.That(siblingPlacement.Order, Is.EqualTo(0));
		});
	}

	private async Task<FolderEntity> AddFolder(string name, Guid? parentId, int order, bool isDefault = false)
	{
		var folder = new FolderEntity
		{
			Id = Guid.NewGuid(),
			ProfileId = _profileId,
			Name = name,
			ParentId = parentId,
			Order = order,
			Rows = 3,
			Columns = 5,
			IsDefault = isDefault,
			CreatedAt = DateTime.UtcNow
		};
		await new FolderCache(_cache).AddOrUpdate(folder);
		return folder;
	}
}
