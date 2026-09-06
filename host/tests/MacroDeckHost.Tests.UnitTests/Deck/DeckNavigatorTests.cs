using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Devices.Surfaces;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Deck;

[TestFixture]
public class DeckNavigatorTests
{
	private static readonly string[] _expectedProfileIds = ["p1", "obs::main"];
	private static readonly string[] _expectedProfileLabels = ["Alpha", "Beta (OBS)"];

	private ProfileCache _cache = null!;
	private FolderCache _folderCache = null!;
	private RecordingTransport _transport = null!;
	private FakeProfileRegistry _profileRegistry = null!;
	private RecordingDeviceSurfaces _deviceSurfaces = null!;
	private DeckNavigator _navigator = null!;

	[SetUp]
	public async Task SetUp()
	{
		_cache = new ProfileCache(new InMemoryProfileStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		_folderCache = new FolderCache(_cache);
		_transport = new RecordingTransport();
		_profileRegistry = new FakeProfileRegistry();
		_deviceSurfaces = new RecordingDeviceSurfaces();
		_navigator = new DeckNavigator(_transport,
			_folderCache,
			_cache,
			_profileRegistry,
			() => _deviceSurfaces);
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task ChangeFolderAsync_UnknownFolderId_PublishesNoNavigationEvent()
	{
		await _navigator.ChangeFolderAsync(Guid.NewGuid().ToString());

		Assert.That(_transport.Sent, Is.Empty);
	}

	[Test]
	public async Task ChangeFolderAsync_MalformedFolderId_PublishesNoNavigationEvent()
	{
		await _navigator.ChangeFolderAsync("not-a-guid");

		Assert.That(_transport.Sent, Is.Empty);
	}

	[Test]
	public async Task ChangeFolderAsync_KnownFolderId_StillPublishesTheNavigationEvent()
	{
		var profileId = Guid.NewGuid();
		await _cache.AddOrUpdate(new ProfileEntity { Id = profileId, Name = "P" });
		var folder = new FolderEntity
		{
			Id = Guid.NewGuid(), ProfileId = profileId, Name = "F", Order = 0, Rows = 3, Columns = 5
		};
		await _folderCache.AddOrUpdate(folder);

		await _navigator.ChangeFolderAsync(folder.Id.ToString());

		Assert.That(_transport.Sent, Has.Count.EqualTo(1));
		var evt = (FolderNavigationEvent)_transport.Sent.Single();
		Assert.Multiple(() =>
		{
			Assert.That(evt.Command, Is.EqualTo("changeTo"));
			Assert.That(evt.FolderId, Is.EqualTo(folder.Id.ToString()));
			Assert.That(evt.ProfileId, Is.EqualTo(profileId.ToString()));
		});
	}

	[Test]
	public async Task ChangeFolderOnDeviceAsync_UnknownFolderId_ReturnsFalseAndSendsNothing()
	{
		var result = await _navigator.ChangeFolderOnDeviceAsync(Guid.NewGuid(),
			Guid.NewGuid(),
			Guid.NewGuid(),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.False);
			Assert.That(_transport.GroupMessages, Is.Empty);
		});
	}

	[Test]
	public async Task ChangeFolderOnDeviceAsync_KnownFolderId_SendsToTheDeviceGroupWithTheToken()
	{
		var profileId = Guid.NewGuid();
		await _cache.AddOrUpdate(new ProfileEntity { Id = profileId, Name = "P" });
		var folder = new FolderEntity
		{
			Id = Guid.NewGuid(), ProfileId = profileId, Name = "F", Order = 0, Rows = 3, Columns = 5
		};
		await _folderCache.AddOrUpdate(folder);
		var deviceId = Guid.NewGuid();
		var navigationToken = Guid.NewGuid();

		var result = await _navigator.ChangeFolderOnDeviceAsync(deviceId,
			folder.Id,
			navigationToken,
			CancellationToken.None);

		Assert.That(result, Is.True);
		Assert.That(_transport.GroupMessages, Has.Count.EqualTo(1));
		var (group, message) = _transport.GroupMessages.Single();
		var evt = (FolderNavigationEvent)message;
		Assert.Multiple(() =>
		{
			Assert.That(group, Is.EqualTo(UiDeviceGroups.For(deviceId)));
			Assert.That(evt.Command, Is.EqualTo("changeTo"));
			Assert.That(evt.FolderId, Is.EqualTo(folder.Id.ToString()));
			Assert.That(evt.ProfileId, Is.EqualTo(profileId.ToString()));
			Assert.That(evt.NavigationToken, Is.EqualTo(navigationToken.ToString("D")));
		});
	}

	[Test]
	public async Task ChangeProfileAsync_UnknownProfileId_PublishesNoNavigationEvent()
	{
		await _navigator.ChangeProfileAsync("does-not-exist");

		Assert.That(_transport.Sent, Is.Empty);
	}

	[Test]
	public async Task ChangeProfileAsync_PersistedProfile_PublishesChangeToAtItsStartFolder()
	{
		_profileRegistry.AddProfile("p1", "Profile One")
			.SetFolders("p1", new Folder { Id = "f1", ParentId = null, Order = 0, IsDefault = true });

		await _navigator.ChangeProfileAsync("p1");

		Assert.That(_transport.Sent, Has.Count.EqualTo(1));
		var evt = (FolderNavigationEvent)_transport.Sent.Single();
		Assert.Multiple(() =>
		{
			Assert.That(evt.Command, Is.EqualTo("changeTo"));
			Assert.That(evt.FolderId, Is.EqualTo("f1"));
			Assert.That(evt.ProfileId, Is.EqualTo("p1"));
		});
	}

	[Test]
	public async Task ChangeProfileAsync_VirtualProfile_PublishesChangeToAtItsIntegrationQualifiedStartFolder()
	{
		_profileRegistry.AddProfile("obs::main", "OBS / Main")
			.SetFolders("obs::main", new Folder { Id = "obs::folder-1", ParentId = null, Order = 0 });

		await _navigator.ChangeProfileAsync("obs::main");

		Assert.That(_transport.Sent, Has.Count.EqualTo(1));
		var evt = (FolderNavigationEvent)_transport.Sent.Single();
		Assert.Multiple(() =>
		{
			Assert.That(evt.Command, Is.EqualTo("changeTo"));
			Assert.That(evt.FolderId, Is.EqualTo("obs::folder-1"));
			Assert.That(evt.ProfileId, Is.EqualTo("obs::main"));
		});
	}

	[Test]
	public async Task ChangeProfileAsync_WithOriginClientId_SendsOnlyToThatClientsGroup()
	{
		_profileRegistry.AddProfile("p1", "Profile One")
			.SetFolders("p1", new Folder { Id = "f1", ParentId = null, Order = 0, IsDefault = true });

		await _navigator.ChangeProfileAsync("p1", originClientId: "client-1");

		Assert.That(_transport.GroupMessages, Has.Count.EqualTo(1));
		var (group, _) = _transport.GroupMessages.Single();
		Assert.That(group, Is.EqualTo(UiClientGroups.For("client-1")));
	}

	[Test]
	public async Task ChangeProfileAsync_WithoutOriginClientId_BroadcastsToEveryClient()
	{
		_profileRegistry.AddProfile("p1", "Profile One")
			.SetFolders("p1", new Folder { Id = "f1", ParentId = null, Order = 0, IsDefault = true });

		await _navigator.ChangeProfileAsync("p1");

		Assert.Multiple(() =>
		{
			Assert.That(_transport.Sent, Has.Count.EqualTo(1));
			Assert.That(_transport.GroupMessages, Is.Empty);
		});
	}

	[Test]
	public async Task ChangeProfileOnDeviceAsync_UnknownProfileId_ReturnsFalseAndSendsNothing()
	{
		var result = await _navigator.ChangeProfileOnDeviceAsync(Guid.NewGuid(),
			"does-not-exist",
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.False);
			Assert.That(_transport.GroupMessages, Is.Empty);
		});
	}

	[Test]
	public async Task ChangeProfileOnDeviceAsync_KnownProfileId_SendsToTheDeviceGroupWithNoNavigationToken()
	{
		_profileRegistry.AddProfile("p1", "Profile One")
			.SetFolders("p1", new Folder { Id = "f1", ParentId = null, Order = 0, IsDefault = true });
		var deviceId = Guid.NewGuid();

		var result = await _navigator.ChangeProfileOnDeviceAsync(deviceId, "p1", CancellationToken.None);

		Assert.That(result, Is.True);
		Assert.That(_transport.GroupMessages, Has.Count.EqualTo(1));
		var (group, message) = _transport.GroupMessages.Single();
		var evt = (FolderNavigationEvent)message;
		Assert.Multiple(() =>
		{
			Assert.That(group, Is.EqualTo(UiDeviceGroups.For(deviceId)));
			Assert.That(evt.Command, Is.EqualTo("changeTo"));
			Assert.That(evt.FolderId, Is.EqualTo("f1"));
			Assert.That(evt.ProfileId, Is.EqualTo("p1"));
			Assert.That(evt.NavigationToken, Is.Null);
		});
	}

	[Test]
	public async Task ChangeFolderAsync_WithADeviceOrigin_NavigatesThatSessionAndNoWebSocketClient()
	{
		var deviceId = Guid.NewGuid();
		_deviceSurfaces.Open = true;

		// Deliberately a folder the folder cache does not know: a device session validates against its
		// own profile's folders, which is what lets a device navigate a virtual profile.
		await _navigator.ChangeFolderAsync("obs::folder-1", DeviceOrigin.For(deviceId));

		Assert.Multiple(() =>
		{
			Assert.That(_deviceSurfaces.Navigations, Has.Count.EqualTo(1));
			Assert.That(_deviceSurfaces.Navigations[0].DeviceId, Is.EqualTo(deviceId));
			Assert.That(_deviceSurfaces.Navigations[0].Command.Command, Is.EqualTo("changeTo"));
			Assert.That(_deviceSurfaces.Navigations[0].Command.FolderId, Is.EqualTo("obs::folder-1"));
			Assert.That(_transport.Sent, Is.Empty);
			Assert.That(_transport.GroupMessages, Is.Empty);
		});
	}

	[Test]
	public async Task ChangeProfileAsync_WithADeviceOrigin_NavigatesThatSessionAndNoWebSocketClient()
	{
		_profileRegistry.AddProfile("p1", "Profile One")
			.SetFolders("p1", new Folder { Id = "f1", ParentId = null, Order = 0, IsDefault = true });
		var deviceId = Guid.NewGuid();

		await _navigator.ChangeProfileAsync("p1", DeviceOrigin.For(deviceId));

		Assert.Multiple(() =>
		{
			Assert.That(_deviceSurfaces.Navigations, Has.Count.EqualTo(1));
			Assert.That(_deviceSurfaces.Navigations[0].DeviceId, Is.EqualTo(deviceId));
			Assert.That(_deviceSurfaces.Navigations[0].Command.FolderId, Is.EqualTo("f1"));
			Assert.That(_deviceSurfaces.Navigations[0].Command.ProfileId, Is.EqualTo("p1"));
			Assert.That(_transport.Sent, Is.Empty);
			Assert.That(_transport.GroupMessages, Is.Empty);
		});
	}

	[Test]
	public async Task GoToParentAsync_WithADeviceOrigin_NavigatesThatSessionAndNoWebSocketClient()
	{
		var deviceId = Guid.NewGuid();

		await _navigator.GoToParentAsync(DeviceOrigin.For(deviceId));

		Assert.Multiple(() =>
		{
			Assert.That(_deviceSurfaces.Navigations, Has.Count.EqualTo(1));
			Assert.That(_deviceSurfaces.Navigations[0].DeviceId, Is.EqualTo(deviceId));
			Assert.That(_deviceSurfaces.Navigations[0].Command.Command, Is.EqualTo("parent"));
			Assert.That(_transport.Sent, Is.Empty);
			Assert.That(_transport.GroupMessages, Is.Empty);
		});
	}

	[Test]
	public async Task GoBackAsync_WithADeviceOrigin_NavigatesThatSessionAndNoWebSocketClient()
	{
		var deviceId = Guid.NewGuid();

		await _navigator.GoBackAsync(DeviceOrigin.For(deviceId));

		Assert.Multiple(() =>
		{
			Assert.That(_deviceSurfaces.Navigations, Has.Count.EqualTo(1));
			Assert.That(_deviceSurfaces.Navigations[0].DeviceId, Is.EqualTo(deviceId));
			Assert.That(_deviceSurfaces.Navigations[0].Command.Command, Is.EqualTo("back"));
			Assert.That(_transport.Sent, Is.Empty);
			Assert.That(_transport.GroupMessages, Is.Empty);
		});
	}

	[Test]
	public void GetProfiles_ListsPersistedAndVirtualProfilesOrderedByLabel()
	{
		_profileRegistry.AddProfile("p1", "Alpha");
		_profileRegistry.AddProfile("obs::main", "Beta (OBS)");

		var profiles = _navigator.GetProfiles();

		Assert.Multiple(() =>
		{
			Assert.That(profiles.Select(p => p.Id), Is.EqualTo(_expectedProfileIds));
			Assert.That(profiles.Select(p => p.Label), Is.EqualTo(_expectedProfileLabels));
		});
	}

	private sealed class RecordingDeviceSurfaces : IDeviceSurfaceService
	{
		public List<(Guid DeviceId, DeckNavigationCommand Command)> Navigations { get; } = [];

		/// <summary>False by default, so a navigation without a device origin takes the client path.</summary>
		public bool Open { get; set; }

		public bool IsOpen(Guid deviceId) => Open;

		public Task OpenAsync(Guid deviceId,
			string providerId,
			string providerDeviceId,
			CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task CloseAsync(Guid deviceId, string? reason, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task<bool> NavigateAsync(Guid deviceId,
			DeckNavigationCommand command,
			CancellationToken cancellationToken = default)
		{
			Navigations.Add((deviceId, command));
			return Task.FromResult(true);
		}

		public Task<DeviceInteractionOutcome> SubmitInteractionAsync(Guid deviceId,
			DeviceInteraction interaction,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(DeviceInteractionOutcome.Reject(DeviceSurfaceErrorCodes.SessionNotFound));

		public Task<DeviceIconImage?> GetIconAsync(Guid deviceId,
			string iconId,
			int? size = null,
			string? knownETag = null,
			CancellationToken cancellationToken = default) => Task.FromResult<DeviceIconImage?>(null);

		public Task<DeviceWidgetIconImage?> GetWidgetIconAsync(Guid deviceId,
			string widgetId,
			string? knownETag = null,
			CancellationToken cancellationToken = default) => Task.FromResult<DeviceWidgetIconImage?>(null);

		public Task InvalidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task InvalidateAsync(Guid deviceId, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task SetPresenceAsync(Guid deviceId, bool online, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
	}

	private sealed class RecordingTransport : IUiTransport
	{
		public Task SendToConnection<T>(string connectionId, T message, CancellationToken cancellationToken = default)
			where T : class
			=> Task.CompletedTask;

		public Task AddToGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task RemoveFromGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public List<object> Sent { get; } = [];

		public List<(string Group, object Message)> GroupMessages { get; } = [];

		public Task Send<T>(T message, CancellationToken cancellationToken = default)
			where T : class
		{
			Sent.Add(message);
			return Task.CompletedTask;
		}

		public Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
			where T : class
		{
			Sent.Add(message);
			GroupMessages.Add((group, message));
			return Task.CompletedTask;
		}
	}
}
