using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Operations;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Store;

[TestFixture]
internal sealed class StoreOperationUserNotificationHandlerTests
{
	private UserNotificationStore _notifications = null!;
	private StoreOperationUserNotificationHandler _handler = null!;

	[SetUp]
	public void SetUp()
	{
		_notifications = new UserNotificationStore();
		_handler = new StoreOperationUserNotificationHandler(_notifications,
			TestLocalization.ScopeFactory,
			TestLocalization.Resolver);
	}

	[Test]
	public async Task Installing_an_older_version_over_a_newer_one_is_announced_as_such()
	{
		var entry = await Raise(Operation("1.0.0", previousVersion: "2.0.0", StoreOperationState.Completed));

		Assert.That(entry.Title, Is.EqualTo("Installed an older version of SoundBox"));
	}

	[Test]
	public async Task A_failed_downgrade_is_announced_as_a_failed_downgrade()
	{
		var entry = await Raise(Operation("1.0.0", previousVersion: "2.0.0", StoreOperationState.Failed) with
		{
			Error = StoreOperationError.DownloadFailed,
			ErrorMessage = "offline"
		});

		Assert.Multiple(() =>
		{
			Assert.That(entry.Title, Is.EqualTo("Failed to install an older version of SoundBox"));
			Assert.That(entry.Message, Is.EqualTo("offline"));
		});
	}

	[Test]
	public async Task An_ordinary_update_keeps_its_update_wording()
	{
		var completed = await Raise(Operation("2.0.0", previousVersion: "1.0.0", StoreOperationState.Completed));

		Assert.That(completed.Title, Is.EqualTo("Updated SoundBox"));
	}

	[Test]
	public async Task A_plugin_needing_a_newer_Macro_Deck_explains_why_and_leads_to_the_update_settings()
	{
		var entry = await Raise(Operation("1.0.0", previousVersion: null, StoreOperationState.Failed) with
		{
			Kind = StoreOperationKind.Install,
			Error = StoreOperationError.RequiresNewerMacroDeck,
			ErrorMessage = "The plugin needs Macro Deck '>=3.0.0-beta.12'; this host is 3.0.0-beta.11."
		});

		Assert.Multiple(() =>
		{
			Assert.That(entry.Title, Is.EqualTo("Failed to install SoundBox"));
			Assert.That(entry.Message, Is.EqualTo("This version needs a newer version of Macro Deck."));
			Assert.That(entry.Action?.Kind, Is.EqualTo(UserNotificationActionKind.OpenUpdateSettings));
		});
	}

	[TestCase(StoreOperationError.Incompatible, "This version does not work with this version of Macro Deck.")]
	[TestCase(StoreOperationError.VersionNotFound, "This version is not available in the store.")]
	public async Task A_version_refusal_is_explained_in_the_users_language(StoreOperationError error, string message)
	{
		var entry = await Raise(Operation("1.0.0", previousVersion: null, StoreOperationState.Failed) with
		{
			Kind = StoreOperationKind.Install,
			Error = error,
			ErrorMessage = "diagnostic text"
		});

		Assert.Multiple(() =>
		{
			Assert.That(entry.Message, Is.EqualTo(message));
			Assert.That(entry.Action?.Kind, Is.EqualTo(UserNotificationActionKind.OpenExtensionStore));
		});
	}

	private async Task<UserNotification> Raise(StoreOperation operation)
	{
		await _handler.Handle(new StoreOperationProgressNotification(operation), CancellationToken.None);
		return _notifications.Snapshot().Single();
	}

	private static StoreOperation Operation(string version, string? previousVersion, StoreOperationState state) => new()
	{
		Id = Guid.CreateVersion7(),
		Kind = StoreOperationKind.Update,
		ExtensionKind = StoreExtensionKind.Plugin,
		PackageId = "com.acme.soundbox",
		Version = version,
		PreviousVersion = previousVersion,
		DisplayName = "SoundBox",
		State = state
	};
}
