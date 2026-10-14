using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

/// <summary>
/// Opening the folder surface (issue #785): who serves it, and what happens for the two folders that
/// must never open one - a widget grid, and a folder whose provider is gone.
/// </summary>
[TestFixture]
internal sealed class FolderUiSessionTests
{
	private const string ProviderId = "com.example.home";
	private const string DeviceA = "device-a";
	private const string DeviceB = "device-b";

	private ManualTimeProvider _time = null!;
	private UiSessionRegistry _registry = null!;
	private FakeFolderViewFolderCache _folders = null!;
	private IFolderViewRegistry _folderViews = null!;
	private string _folderViewId = null!;
	private UiSessionBroker _broker = null!;
	private FolderUiSessionOpener _opener = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new ManualTimeProvider();
		_registry = new UiSessionRegistry(_time);
		_folders = new FakeFolderViewFolderCache();
		(_folderViews, _folderViewId) = TestFolderViewProviders.WithView(ProviderId);

		_broker = new UiSessionBroker(new StubProviderResolver(ProviderId),
			new RecordingUiSessionTransport(),
			_registry,
			new PluginSessionRegistry(_time, Serilog.Core.Logger.None),
			new StubIntegrationRegistry(),
			_time,
			Serilog.Core.Logger.None);

		_opener = new FolderUiSessionOpener(_folders, _folderViews, _registry, _broker);
	}

	[TearDown]
	public void TearDown()
	{
		_broker.Dispose();
		_registry.Dispose();
	}

	[Test]
	public void AWidgetGridFolder_IsNeverOpenedAsASession()
	{
		var folder = _folders.Add(null);

		var response = Open(folder.Id);

		Assert.Multiple(() =>
		{
			Assert.That(response.Accepted, Is.False);
			Assert.That(response.Code, Is.EqualTo(UiSessionErrorCodes.InvalidPayload));
			Assert.That(response.ViewId, Is.EqualTo(BuiltInFolderViews.WidgetGrid));
		});
	}

	/// <summary>
	/// The client renders its placeholder from this, so the answer has to name the view and has to be a
	/// rejection rather than a fault: nothing is wrong with the folder, its provider is simply away.
	/// </summary>
	[Test]
	public void AFolderWhoseProviderIsGone_IsRejectedAndStillNamesItsView()
	{
		var folder = _folders.Add("com.example.gone::dashboard");

		var response = Open(folder.Id);

		Assert.Multiple(() =>
		{
			Assert.That(response.Accepted, Is.False);
			Assert.That(response.Code, Is.EqualTo(UiSessionErrorCodes.ProviderUnavailable));
			Assert.That(response.ViewId, Is.EqualTo("com.example.gone::dashboard"));
			Assert.That(folder.ViewId, Is.EqualTo("com.example.gone::dashboard"), "the folder must be untouched");
		});
	}

	[Test]
	public void AnUnknownFolder_IsRejected()
	{
		var response = _opener.Open(new OpenFolderUiSessionRequest { FolderId = Guid.NewGuid().ToString() },
			DeviceA);

		Assert.That(response.Accepted, Is.False);
		Assert.That(response.Code, Is.EqualTo(UiSessionErrorCodes.ProviderUnavailable));
	}

	[Test]
	public void AProvidedView_OpensAgainstTheOwningIntegration()
	{
		var folder = _folders.Add(_folderViewId, """{"areaId":"living-room"}""");

		var response = Open(folder.Id);

		Assert.That(response.Accepted, Is.True);

		var session = _registry.Find(response.SessionId)!;
		Assert.Multiple(() =>
		{
			Assert.That(session.ProviderId, Is.EqualTo(ProviderId));
			Assert.That(session.Surface.Kind, Is.EqualTo(UiSurfaceKinds.Folder));
			Assert.That(Attribute(session, UiFolderSurfaceAttributes.FolderId), Is.EqualTo(folder.Id.ToString()));
			Assert.That(Attribute(session, UiFolderSurfaceAttributes.ViewId), Is.EqualTo(_folderViewId));
		});
	}

	/// <summary>The provider cannot read the host's folders, so what the user configured has to arrive
	/// with the request - the same reason a widget's data does.</summary>
	[Test]
	public void TheStoredConfiguration_TravelsOnTheSurface()
	{
		var folder = _folders.Add(_folderViewId, """{"areaId":"living-room"}""");

		var response = Open(folder.Id);
		var session = _registry.Find(response.SessionId)!;

		var configuration = session.Surface.Attributes[UiFolderSurfaceAttributes.Configuration];
		Assert.That(configuration.GetProperty("areaId").GetString(), Is.EqualTo("living-room"));
	}

	[Test]
	public void AFolderWithNoStoredConfiguration_OpensWithAnEmptyOne()
	{
		var folder = _folders.Add(_folderViewId);

		var response = Open(folder.Id);
		var session = _registry.Find(response.SessionId)!;

		Assert.That(session.Surface.Attributes[UiFolderSurfaceAttributes.Configuration].EnumerateObject(),
			Is.Empty);
	}

	/// <summary>
	/// Two folders can share one provider, so a session is matched on the folder rather than on the
	/// provider - reusing by provider alone would show one folder's view inside another.
	/// </summary>
	[Test]
	public void TwoFoldersOnOneProvider_GetSeparateSessions()
	{
		var first = _folders.Add(_folderViewId);
		var second = _folders.Add(_folderViewId);

		var firstResponse = Open(first.Id);
		var secondResponse = Open(second.Id);

		Assert.That(firstResponse.Accepted, Is.True);
		Assert.That(secondResponse.Accepted, Is.True);
		Assert.That(secondResponse.SessionId, Is.Not.EqualTo(firstResponse.SessionId));
	}

	[Test]
	public void ReopeningTheSameFolderForTheSameDevice_ReusesItsSession()
	{
		var folder = _folders.Add(_folderViewId);

		var first = Open(folder.Id);
		var second = Open(folder.Id);

		Assert.That(second.SessionId, Is.EqualTo(first.SessionId));
	}

	[Test]
	public void TwoDevices_OpenTheirOwnSessionForOneFolder()
	{
		var folder = _folders.Add(_folderViewId);

		var deviceA = Open(folder.Id);
		var deviceB = _opener.Open(new OpenFolderUiSessionRequest { FolderId = folder.Id.ToString() }, DeviceB);

		Assert.That(deviceB.SessionId, Is.Not.EqualTo(deviceA.SessionId));
	}

	/// <summary>The client draws Macro Deck's back button from this, so it has to arrive resolved.</summary>
	[Test]
	public void TheResponse_CarriesTheViewsNavigationMode()
	{
		var folder = _folders.Add(_folderViewId);

		Assert.That(Open(folder.Id).Navigation,
			Is.EqualTo(MacroDeck.Sdk.FolderViews.FolderViewNavigation.Default));
	}

	private OpenFolderUiSessionResponse Open(Guid folderId)
	{
		return _opener.Open(new OpenFolderUiSessionRequest { FolderId = folderId.ToString() }, DeviceA);
	}

	private static string? Attribute(UiSessionSnapshot session, string key)
	{
		return session.Surface.Attributes[key].GetString();
	}

	private sealed class StubProviderResolver : IUiSessionProviderResolver
	{
		private readonly string _providerId;

		public StubProviderResolver(string providerId)
		{
			_providerId = providerId;
		}

		public IUiSessionProvider? Resolve(string providerId)
		{
			return string.Equals(providerId, _providerId, StringComparison.Ordinal)
				? new StubFolderUiSessionProvider(providerId)
				: null;
		}
	}

	private sealed class StubFolderUiSessionProvider : IUiSessionProvider
	{
		public StubFolderUiSessionProvider(string providerId)
		{
			ProviderId = providerId;
		}

		public string ProviderId { get; }

		public Task<UiSessionOpenOutcome> OpenAsync(UiSessionOpenCommand command, CancellationToken cancellationToken)
		{
			return Task.FromResult(UiSessionOpenOutcome.Accept(command.UiModelVersion));
		}

		public Task CloseAsync(string sessionId, string reason, CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}

		public Task RequestSnapshotAsync(string sessionId, CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}

		public Task DispatchEventAsync(
			string sessionId,
			UiSessionEventCommand command,
			CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}
	}

	private sealed class FakeFolderViewFolderCache : IFolderCache
	{
		private readonly List<FolderEntity> _folders = [];

		public FolderEntity Add(string? viewId, string? configuration = null)
		{
			var folder = new FolderEntity
			{
				Id = Guid.NewGuid(),
				Name = "Living Room",
				Order = 0,
				ViewId = viewId,
				ViewConfiguration = configuration
			};

			_folders.Add(folder);
			return folder;
		}

		public List<FolderEntity> GetAllFolders()
		{
			return _folders;
		}

		public FolderEntity? GetFolderById(Guid id)
		{
			return _folders.Find(folder => folder.Id == id);
		}

		public Task InitializeCache()
		{
			return Task.CompletedTask;
		}

		public List<FolderEntity> GetFoldersByParentId(Guid? parentId)
		{
			return _folders;
		}

		public List<FolderEntity> GetFoldersByProfileId(Guid profileId)
		{
			return _folders;
		}

		public Task AddOrUpdate(FolderEntity folder)
		{
			return Task.CompletedTask;
		}

		public Task AddOrUpdateRange(IReadOnlyCollection<FolderEntity> folders)
		{
			return Task.CompletedTask;
		}

		public Task<FolderSubtreeRemoval> RemoveSubtree(Guid rootId)
		{
			return Task.FromResult(new FolderSubtreeRemoval(false, [], []));
		}

		public void AddWidget(Guid folderId, WidgetEntity widget)
		{
		}

		public void AddWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
		{
		}

		public void UpdateWidget(Guid folderId, WidgetEntity widget)
		{
		}

		public void UpdateWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
		{
		}

		public void UpdateWidgetPositions(Guid folderId, IReadOnlyList<WidgetPlacement> placements)
		{
		}

		public void RemoveWidget(Guid folderId, Guid widgetId)
		{
		}

		public void RemoveWidgets(Guid folderId, IReadOnlyList<Guid> widgetIds)
		{
		}

		public void ReplaceWidgets(Guid folderId, IReadOnlyList<Guid> removeIds, IReadOnlyList<WidgetEntity> addWidgets)
		{
		}
	}
}
