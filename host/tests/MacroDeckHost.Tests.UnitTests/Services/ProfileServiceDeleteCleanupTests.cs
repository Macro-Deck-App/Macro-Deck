using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class ProfileServiceDeleteCleanupTests
{
	private InMemoryProfileStore _store = null!;
	private ProfileCache _cache = null!;
	private FolderCache _folderCache = null!;
	private RecordingMediator _mediator = null!;
	private FakeSecretService _secrets = null!;
	private ProfileService _service = null!;
	private Guid _doomedId;
	private Guid _keptId;

	[SetUp]
	public async Task SetUp()
	{
		_store = new InMemoryProfileStore();
		_cache = new ProfileCache(_store, new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		_folderCache = new FolderCache(_cache);
		_mediator = new RecordingMediator();
		_secrets = new FakeSecretService();
		_service = new ProfileService(_cache,
			_folderCache,
			new InMemoryDeviceRepository(),
			_mediator,
			new WidgetSecretCloner(_secrets),
			new WidgetSecretScrubber(_secrets),
			new NullWidgetVariableCloner());

		_doomedId = await AddProfile("Doomed");
		_keptId = await AddProfile("Kept");
	}

	[TearDown]
	public void TearDown()
	{
		_cache.Dispose();
	}

	[Test]
	public async Task Deleting_a_profile_scrubs_the_secrets_only_its_widgets_reference()
	{
		var home = await AddFolder(_doomedId, "Home", null, true);
		var child = await AddFolder(_doomedId, "Child", home.Id);
		var keptHome = await AddFolder(_keptId, "Home", null, true);
		var ownSecret = _secrets.Store("own");
		var nestedSecret = _secrets.Store("nested");
		var sharedSecret = _secrets.Store("shared");
		var otherSecret = _secrets.Store("other");
		AddWidget(home.Id, SecretData(ownSecret, sharedSecret));
		AddWidget(child.Id, SecretData(nestedSecret));
		AddWidget(keptHome.Id, SecretData(sharedSecret));
		AddWidget(keptHome.Id, SecretData(otherSecret));

		var result = await _service.Delete(_doomedId);

		Assert.That(result.Success, Is.True);
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(await _secrets.Resolve(ownSecret), Is.Null);
			Assert.That(await _secrets.Resolve(nestedSecret), Is.Null);
			Assert.That(await _secrets.Resolve(sharedSecret), Is.EqualTo("shared"));
			Assert.That(await _secrets.Resolve(otherSecret), Is.EqualTo("other"));
		});
	}

	[Test]
	public async Task Deleting_a_profile_announces_each_widget_and_folder_like_deleting_its_folders()
	{
		var home = await AddFolder(_doomedId, "Home", null, true);
		var second = await AddFolder(_doomedId, "Second", null);
		var left = await AddFolder(_doomedId, "Left", home.Id);
		var right = await AddFolder(_doomedId, "Right", home.Id);
		var leftLeaf = await AddFolder(_doomedId, "Left leaf", left.Id);
		var secondChild = await AddFolder(_doomedId, "Second child", second.Id);
		var keptHome = await AddFolder(_keptId, "Home", null, true);
		var homeWidget = AddWidget(home.Id);
		var leafWidget = AddWidget(leftLeaf.Id);
		var secondChildWidget = AddWidget(secondChild.Id);
		AddWidget(keptHome.Id);

		await _service.Delete(_doomedId);

		var published = _mediator.Published;
		var folderOrder = published.OfType<FolderDeletedNotification>().Select(n => n.FolderId).ToList();
		var childParentPairs = new[] { (left, home), (right, home), (leftLeaf, left), (secondChild, second) };
		Assert.Multiple(() =>
		{
			Assert.That(published.FindIndex(n => n is ProfileDeletedNotification), Is.EqualTo(0));
			Assert.That(published.OfType<ProfileDeletedNotification>().Select(n => n.ProfileId),
				Is.EqualTo(new[] { _doomedId }));
			Assert.That(published.OfType<WidgetDeletedNotification>().Select(n => (n.WidgetId, n.FolderId)),
				Is.EquivalentTo([
					(homeWidget.Id, home.Id),
					(leafWidget.Id, leftLeaf.Id),
					(secondChildWidget.Id, secondChild.Id)
				]));
			Assert.That(folderOrder,
				Is.EquivalentTo([home.Id, second.Id, left.Id, right.Id, leftLeaf.Id, secondChild.Id]));
			Assert.That(published.FindLastIndex(n => n is WidgetDeletedNotification),
				Is.LessThan(published.FindIndex(n => n is FolderDeletedNotification)));
			foreach (var (child, parent) in childParentPairs)
			{
				Assert.That(folderOrder.IndexOf(child.Id),
					Is.LessThan(folderOrder.IndexOf(parent.Id)),
					$"{child.Name} before {parent.Name}");
			}
		});
	}

	[Test]
	public async Task Refusing_to_delete_the_last_profile_scrubs_and_announces_nothing()
	{
		await _cache.Remove(_keptId);
		var home = await AddFolder(_doomedId, "Home", null, true);
		var secret = _secrets.Store("own");
		AddWidget(home.Id, SecretData(secret));

		var result = await _service.Delete(_doomedId);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(result.Error, Is.EqualTo(ProfileError.CannotDeleteLastProfile));
			Assert.That(_mediator.Published, Is.Empty);
			Assert.That(await _secrets.Resolve(secret), Is.EqualTo("own"));
		});
	}

	private static string SecretData(params Guid[] secretIds)
	{
		return "{" + string.Join(",", secretIds.Select((id, i) => $"\"p{i}\":{{\"$secret\":\"{id}\"}}")) + "}";
	}

	private async Task<Guid> AddProfile(string name)
	{
		var profile = new ProfileEntity { Id = Guid.NewGuid(), Name = name, DefaultRows = 3, DefaultColumns = 5 };
		await _cache.AddOrUpdate(profile);
		return profile.Id;
	}

	private async Task<FolderEntity> AddFolder(Guid profileId, string name, Guid? parentId, bool isDefault = false)
	{
		var folder = new FolderEntity
		{
			Id = Guid.NewGuid(),
			ProfileId = profileId,
			Name = name,
			ParentId = parentId,
			Order = _cache.GetFoldersByProfileId(profileId).Count,
			Rows = 3,
			Columns = 5,
			IsDefault = isDefault,
			CreatedAt = DateTime.UtcNow
		};
		await _folderCache.AddOrUpdate(folder);
		return folder;
	}

	private WidgetEntity AddWidget(Guid folderId, string? data = null)
	{
		var widget = new WidgetEntity
		{
			Id = Guid.NewGuid(),
			FolderId = folderId,
			Type = WidgetTypeIds.ActionButton,
			Data = data
		};
		_folderCache.AddWidget(folderId, widget);
		return widget;
	}
}
