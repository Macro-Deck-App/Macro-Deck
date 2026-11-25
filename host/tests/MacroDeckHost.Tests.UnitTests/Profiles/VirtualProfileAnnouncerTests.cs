using MacroDeck.Sdk.Profiles;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using ProfileCreatedEvent = MacroDeckHost.Application.Ui.Transport.Messages.Profiles.ProfileCreatedEvent;
using ProfileDeletedEvent = MacroDeckHost.Application.Ui.Transport.Messages.Profiles.ProfileDeletedEvent;

namespace MacroDeckHost.Tests.UnitTests.Profiles;

[TestFixture]
internal sealed class VirtualProfileAnnouncerTests
{
	private const string Plugin = "com.example.stream-kit";
	private const string ProfileId = Plugin + "::kit";

	private static readonly Type[] _removalOrder =
		[typeof(ProfileDeletedEvent), typeof(WidgetsDeletedEvent), typeof(FolderDeletedEvent)];

	private static readonly Type[] _creationOrder =
		[typeof(ProfileCreatedEvent), typeof(FolderCreatedEvent), typeof(WidgetsCreatedEvent)];

	private FakeProfileProviderIntegration _provider = null!;
	private FakeProfileProviderIntegration _other = null!;
	private ConfigurableIntegrationRegistry _integrations = null!;
	private ProfileRegistry _profiles = null!;
	private ObservingUiTransport _transport = null!;
	private RecordingMediator _mediator = null!;
	private UiSessionRegistry _sessions = null!;
	private VirtualProfileAnnouncer _announcer = null!;
	private VirtualProfilesChangedHandler _handler = null!;

	[SetUp]
	public async Task SetUp()
	{
		_other = new FakeProfileProviderIntegration("com.example.other");
		_provider = new FakeProfileProviderIntegration(Plugin, Kit(Widget("keep", 0), Widget("old", 1)));
		_integrations = new ConfigurableIntegrationRegistry([_other, _provider]);
		_profiles = new ProfileRegistry(new EmptyProfileCache(),
			new FakeFolderCache(),
			_integrations,
			new WidgetTypeRegistry(new RecordingMediator()),
			TestDeviceLayoutConstraintTracker.Build(),
			Serilog.Core.Logger.None,
			TestColors.None);
		_transport = new ObservingUiTransport();
		_mediator = new RecordingMediator();
		_sessions = new UiSessionRegistry(new ManualTimeProvider());
		_announcer = TestVirtualProfileAnnouncer.Create(_profiles,
			_integrations,
			_transport,
			_sessions,
			new WidgetUiProviderRegistry(new FakeFolderCache(), [], () => null!, Serilog.Core.Logger.None),
			_mediator);
		_handler = new VirtualProfilesChangedHandler(_announcer);

		await _handler.Handle(new IntegrationStateChangedNotification(Plugin), CancellationToken.None);
		_transport.Sent.Clear();
		_mediator.Published.Clear();
	}

	[TearDown]
	public void TearDown()
	{
		_announcer.Dispose();
		_sessions.Dispose();
	}

	[Test]
	public async Task A_widget_the_plugin_drops_and_one_it_adds_reach_every_connection_under_their_virtual_ids()
	{
		var oldId = WidgetId("old");
		_provider.Profiles = [Kit(Widget("keep", 0), Widget("new", 2))];

		await PluginChangedProfiles();

		var newId = WidgetId("new");
		var deleted = _transport.Sent.OfType<WidgetsDeletedEvent>().Single();
		var created = _transport.Sent.OfType<WidgetsCreatedEvent>().Single();
		Assert.Multiple(() =>
		{
			Assert.That(_transport.Sent.Select(m => m.GetType()),
				Is.EqualTo(new[] { typeof(WidgetsDeletedEvent), typeof(WidgetsCreatedEvent) }));
			Assert.That(deleted.FolderId, Is.EqualTo(MainFolderId()));
			Assert.That(deleted.WidgetIds, Is.EqualTo(new[] { oldId }));
			Assert.That(created.FolderId, Is.EqualTo(MainFolderId()));
			Assert.That(created.Widgets.Select(w => w.Id), Is.EqualTo(new[] { newId }));
			Assert.That(_transport.GroupMessages, Is.Empty);
			Assert.That(_mediator.Published.OfType<VirtualProfilesAnnouncedNotification>().Single().IntegrationId,
				Is.EqualTo(Plugin));
		});
	}

	[Test]
	public async Task A_folder_the_plugin_adds_and_later_removes_is_announced_both_times()
	{
		_provider.Profiles =
		[
			new VirtualProfileDescriptor("kit",
				"Kit",
				ProfileLayout.Grid(2, 4),
				[
					new VirtualFolderDescriptor("main", "Main", [Widget("keep", 0), Widget("old", 1)]),
					new VirtualFolderDescriptor("extra", "Extra", [], "main")
				])
		];

		await PluginChangedProfiles();
		var extraId = _profiles.GetFoldersForProfile(ProfileId).Single(f => f.Name == "Extra").Id;
		var created = _transport.Sent.OfType<FolderCreatedEvent>().Single();
		_transport.Sent.Clear();

		_provider.Profiles = [Kit(Widget("keep", 0), Widget("old", 1))];
		await PluginChangedProfiles();

		Assert.Multiple(() =>
		{
			Assert.That(created.Folder.Id, Is.EqualTo(extraId));
			Assert.That(created.Folder.ParentId, Is.EqualTo(MainFolderId()));
			Assert.That(_transport.Sent.OfType<FolderDeletedEvent>().Single().FolderId, Is.EqualTo(extraId));
		});
	}

	[Test]
	public async Task Disabling_the_integration_removes_its_profile_first_and_enabling_brings_it_back()
	{
		var folderId = MainFolderId();
		_integrations.SetEnabled(Plugin, false);
		await _handler.Handle(new IntegrationStateChangedNotification(Plugin), CancellationToken.None);
		var onDisable = _transport.Sent.ToList();
		_transport.Sent.Clear();

		_integrations.SetEnabled(Plugin, true);
		await _handler.Handle(new IntegrationStateChangedNotification(Plugin), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(onDisable.Select(m => m.GetType()),
				Is.EqualTo(_removalOrder));
			Assert.That(((ProfileDeletedEvent)onDisable[0]).ProfileId, Is.EqualTo(ProfileId));
			Assert.That(((FolderDeletedEvent)onDisable[2]).FolderId, Is.EqualTo(folderId));
			Assert.That(_transport.Sent.Select(m => m.GetType()),
				Is.EqualTo(_creationOrder));
			Assert.That(_transport.Sent.OfType<ProfileCreatedEvent>().Single().Profile.Id, Is.EqualTo(ProfileId));
			Assert.That(_transport.Sent.OfType<WidgetsCreatedEvent>().Single().Widgets, Has.Count.EqualTo(2));
		});
	}

	[Test]
	public async Task Uninstalling_the_plugin_removes_its_profile_from_every_connection()
	{
		await _integrations.UnregisterAsync(Plugin);

		await _handler.Handle(new IntegrationCatalogChangedNotification(Plugin), CancellationToken.None);

		Assert.That(_transport.Sent.Select(m => m.GetType()), Is.EqualTo(_removalOrder));
	}

	[Test]
	public async Task Nothing_is_announced_when_nothing_the_plugin_serves_changed()
	{
		_other.Profiles = [new VirtualProfileDescriptor("first", "First", ProfileLayout.Grid(1, 1), [])];

		await PluginChangedProfiles();
		await _handler.Handle(new IntegrationStateChangedNotification(Plugin), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_transport.Sent, Is.Empty);
			Assert.That(_mediator.Published, Is.Empty);
		});
	}

	[Test]
	public async Task A_widget_the_plugin_moves_is_announced_as_updated()
	{
		_provider.Profiles = [Kit(Widget("keep", 3), Widget("old", 1))];

		await PluginChangedProfiles();

		var updated = _transport.Sent.OfType<WidgetsUpdatedEvent>().Single();
		Assert.Multiple(() =>
		{
			Assert.That(_transport.Sent, Has.Count.EqualTo(1));
			Assert.That(updated.Widgets.Single().Id, Is.EqualTo(WidgetId("keep")));
			Assert.That(updated.Widgets.Single().PositionX, Is.EqualTo(3));
		});
	}

	[Test]
	public async Task An_integration_that_provides_no_profiles_announces_nothing()
	{
		await _handler.Handle(new IntegrationStateChangedNotification("com.example.unrelated"), CancellationToken.None);

		Assert.That(_transport.Sent, Is.Empty);
	}

	private Task PluginChangedProfiles()
	{
		return _handler.Handle(new VirtualProfilesChangedNotification(Plugin, [], _provider.Profiles),
				CancellationToken.None)
			.AsTask();
	}

	private string MainFolderId()
	{
		return _profiles.GetFoldersForProfile(ProfileId).Single(f => f.Name == "Main").Id;
	}

	private string WidgetId(string localId)
	{
		return _profiles.GetFoldersForProfile(ProfileId)
			.SelectMany(f => f.Widgets)
			.Single(w => w.Id.EndsWith(":" + localId, StringComparison.Ordinal))
			.Id;
	}

	private static VirtualWidgetDescriptor Widget(string id, int x)
	{
		return new VirtualWidgetDescriptor(id, "ActionButton", x, 0);
	}

	private static VirtualProfileDescriptor Kit(params VirtualWidgetDescriptor[] widgets)
	{
		return new VirtualProfileDescriptor("kit",
			"Kit",
			ProfileLayout.Grid(2, 4),
			[new VirtualFolderDescriptor("main", "Main", widgets)]);
	}
}
