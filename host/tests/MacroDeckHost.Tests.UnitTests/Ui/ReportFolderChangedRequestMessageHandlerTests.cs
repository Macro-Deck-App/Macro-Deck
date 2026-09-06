using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;

namespace MacroDeckHost.Tests.UnitTests.Ui;

[TestFixture]
public class ReportFolderChangedRequestMessageHandlerTests
{
	private StubFolderCache _folderCache = null!;
	private RecordingEventBus _bus = null!;
	private RecordingApplicationFocusCoordinator _coordinator = null!;
	private FakeHostLockState _lockState = null!;
	private ReportFolderChangedRequestMessageHandler _handler = null!;

	[SetUp]
	public void SetUp()
	{
		_folderCache = new StubFolderCache();
		_bus = new RecordingEventBus();
		_coordinator = new RecordingApplicationFocusCoordinator();
		_lockState = new FakeHostLockState();
		_handler = new ReportFolderChangedRequestMessageHandler(_folderCache, _bus, _coordinator, _lockState);
	}

	[Test]
	public async Task Locked_PublishesNoOccurrenceAndRefuses()
	{
		var folder = _folderCache.AddFolder();
		_lockState.IsLocked = true;

		var response = await _handler.Handle(
			new ReportFolderChangedRequest { FolderId = folder.Id.ToString(), ClientId = "tab-1" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error?.Code, Is.EqualTo("HOST_LOCKED"));
			Assert.That(_bus.Published, Is.Empty);
			Assert.That(_coordinator.FolderReports, Is.Empty);
		});
	}

	[Test]
	public async Task NormalReport_PublishesTheFolderChangedOccurrenceWithTheUnchangedPayload()
	{
		var folder = _folderCache.AddFolder();

		await _handler.Handle(new ReportFolderChangedRequest { FolderId = folder.Id.ToString(), ClientId = "tab-1" },
			CancellationToken.None);

		Assert.That(_bus.Published, Has.Count.EqualTo(1));
		var occurrence = _bus.Published.Single();
		Assert.Multiple(() =>
		{
			Assert.That(occurrence.Parameters["folderId"], Is.EqualTo(folder.Id.ToString()));
			Assert.That(occurrence.Parameters["folderName"], Is.EqualTo(folder.Name));
			Assert.That(occurrence.Parameters["profileId"], Is.EqualTo(folder.ProfileId.ToString()));
			Assert.That(occurrence.Parameters["clientId"], Is.EqualTo("tab-1"));
		});
	}

	[Test]
	public async Task NormalReport_CarriesTheStableDeviceIdBesideTheClientId()
	{
		// A client id identifies a connection; a user filtering on "which device" means the registered
		// one, and the id has to be the same value the device options source offers.
		var folder = _folderCache.AddFolder();
		var deviceId = Guid.NewGuid();

		await _handler.Handle(new ReportFolderChangedRequest
			{
				FolderId = folder.Id.ToString(),
				ClientId = "tab-1",
				DeviceId = deviceId
			},
			CancellationToken.None);

		var occurrence = _bus.Published.Single();
		Assert.Multiple(() =>
		{
			Assert.That(occurrence.Parameters["deviceId"], Is.EqualTo(deviceId.ToString()));
			Assert.That(occurrence.Parameters["clientId"], Is.EqualTo("tab-1"));
		});
	}

	[Test]
	public async Task NormalReport_FromSomethingThatIsNotADevice_CarriesNoDeviceId()
	{
		// The desktop loopback UI is not a registered device. Reporting an empty string instead would
		// make "device is <blank>" match it, which is not what an unset device means.
		var folder = _folderCache.AddFolder();

		await _handler.Handle(new ReportFolderChangedRequest { FolderId = folder.Id.ToString(), ClientId = "tab-1" },
			CancellationToken.None);

		Assert.That(_bus.Published.Single().Parameters["deviceId"], Is.Null);
	}

	[Test]
	public async Task ResyncReport_PublishesNoOccurrence()
	{
		var folder = _folderCache.AddFolder();

		await _handler.Handle(new ReportFolderChangedRequest { FolderId = folder.Id.ToString(), IsResync = true },
			CancellationToken.None);

		Assert.That(_bus.Published, Is.Empty);
	}

	[Test]
	public async Task UnknownFolder_PublishesNoOccurrenceAndStillSucceeds()
	{
		var response = await _handler.Handle(new ReportFolderChangedRequest { FolderId = Guid.NewGuid().ToString() },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(_bus.Published, Is.Empty);
		});
	}

	[Test]
	public async Task NonGuidFolderId_PublishesNoOccurrenceAndStillSucceeds()
	{
		var response = await _handler.Handle(new ReportFolderChangedRequest { FolderId = "virtual-folder" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(_bus.Published, Is.Empty);
		});
	}

	[Test]
	public async Task DeviceIdPresent_NotifiesTheCoordinatorWithTheTokenAndResyncFlag()
	{
		var folder = _folderCache.AddFolder();
		var deviceId = Guid.NewGuid();
		var token = Guid.NewGuid().ToString("D");

		await _handler.Handle(new ReportFolderChangedRequest
			{
				FolderId = folder.Id.ToString(),
				DeviceId = deviceId,
				NavigationToken = token,
				IsResync = true
			},
			CancellationToken.None);

		Assert.That(_coordinator.FolderReports, Has.Count.EqualTo(1));
		var report = _coordinator.FolderReports.Single();
		Assert.Multiple(() =>
		{
			Assert.That(report.DeviceId, Is.EqualTo(deviceId));
			Assert.That(report.FolderId, Is.EqualTo(folder.Id));
			Assert.That(report.NavigationToken, Is.EqualTo(token));
			Assert.That(report.IsResync, Is.True);
		});
	}

	[Test]
	public async Task DeviceIdNull_DoesNotNotifyTheCoordinator()
	{
		var folder = _folderCache.AddFolder();

		await _handler.Handle(new ReportFolderChangedRequest { FolderId = folder.Id.ToString() },
			CancellationToken.None);

		Assert.That(_coordinator.FolderReports, Is.Empty);
	}
}
