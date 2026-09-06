using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Infrastructure.Notifications;

namespace MacroDeckHost.Tests.UnitTests.Notifications;

public class IconImportUserNotificationHandlerTests
{
	private static readonly Guid _packId = Guid.NewGuid();

	[TestCase(IconImportBatchState.Discovering)]
	[TestCase(IconImportBatchState.Processing)]
	public async Task A_running_import_is_announced_once(IconImportBatchState state)
	{
		var store = new UserNotificationStore();
		var cache = new FakeIconPackCache();
		cache.AddPack(_packId, "My Pack");
		var handler = CreateHandler(store, cache);

		await handler.Handle(Notification(Batch(state), processed: 1, failed: 0), CancellationToken.None);

		var notifications = store.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(1));
			Assert.That(notifications[0].Severity, Is.EqualTo(UserNotificationSeverity.Info));
			Assert.That(notifications[0].Title, Is.EqualTo("Importing icons into My Pack"));
			Assert.That(notifications[0].Progress, Is.EqualTo(new UserNotificationProgress(1, 1)));
		});
	}

	[Test]
	public async Task Progress_ticks_update_progress_without_replacing_the_entry()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store, new FakeIconPackCache());
		var batch = Batch(IconImportBatchState.Processing);

		await handler.Handle(Notification(batch, processed: 1, failed: 0), CancellationToken.None);
		var announced = store.Snapshot()[0];

		for (var processed = 2; processed <= 20; processed++)
		{
			await handler.Handle(Notification(batch, processed, failed: 0), CancellationToken.None);
		}

		var notifications = store.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(1));
			Assert.That(notifications[0].Sequence, Is.EqualTo(announced.Sequence), "the entry was replaced");
			Assert.That(notifications[0].Progress, Is.EqualTo(new UserNotificationProgress(20, 20)));
		});
	}

	[Test]
	public async Task A_progress_tick_arriving_after_the_outcome_does_not_resurrect_the_running_entry()
	{
		var store = new UserNotificationStore();
		var cache = new FakeIconPackCache();
		cache.AddPack(_packId, "My Pack");
		var handler = CreateHandler(store, cache);
		var batch = Batch(IconImportBatchState.Processing);

		await handler.Handle(Notification(batch, processed: 3, failed: 0), CancellationToken.None);
		var completed = Batch(IconImportBatchState.Completed);
		completed.Id = batch.Id;
		await handler.Handle(Notification(completed, processed: 7, failed: 0), CancellationToken.None);

		await handler.Handle(Notification(batch, processed: 6, failed: 0), CancellationToken.None);

		Assert.That(store.Snapshot(), Is.Empty, "a straggling tick re-announced a finished import");
	}

	[Test]
	public async Task A_clean_outcome_retires_the_running_entry_and_says_nothing()
	{
		var store = new UserNotificationStore();
		var cache = new FakeIconPackCache();
		cache.AddPack(_packId, "My Pack");
		var handler = CreateHandler(store, cache);
		var batch = Batch(IconImportBatchState.Processing);

		await handler.Handle(Notification(batch, processed: 3, failed: 0), CancellationToken.None);
		Assert.That(store.Snapshot(), Has.Count.EqualTo(1), "the running import announces itself");

		batch.State = IconImportBatchState.Completed;
		await handler.Handle(Notification(batch, processed: 7, failed: 0), CancellationToken.None);

		Assert.That(store.Snapshot(), Is.Empty);
	}

	[Test]
	public async Task Completed_with_nothing_imported_but_files_skipped_still_says_so()
	{
		var store = new UserNotificationStore();
		var cache = new FakeIconPackCache();
		cache.AddPack(_packId, "My Pack");
		var handler = CreateHandler(store, cache);

		var batch = Batch(IconImportBatchState.Completed);
		batch.Skipped = 3;

		await handler.Handle(Notification(batch, processed: 0, failed: 0), CancellationToken.None);

		var notifications = store.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(1));
			Assert.That(notifications[0].Severity, Is.EqualTo(UserNotificationSeverity.Info));
			Assert.That(notifications[0].Title, Is.EqualTo("Nothing to import into My Pack"));
			Assert.That(notifications[0].Message, Does.Contain("3 files"));
		});
	}

	[Test]
	public async Task Completed_with_nothing_imported_and_nothing_skipped_still_says_so()
	{
		var store = new UserNotificationStore();
		var cache = new FakeIconPackCache();
		cache.AddPack(_packId, "My Pack");
		var handler = CreateHandler(store, cache);

		await handler.Handle(Notification(Batch(IconImportBatchState.Completed), processed: 0, failed: 0),
			CancellationToken.None);

		var notifications = store.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(1));
			Assert.That(notifications[0].Title, Is.EqualTo("Nothing to import into My Pack"));
			Assert.That(notifications[0].Message, Does.Contain("could be imported"));
		});
	}

	[Test]
	public async Task Completed_with_some_imported_and_some_skipped_still_says_nothing()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store, new FakeIconPackCache());

		var batch = Batch(IconImportBatchState.Completed);
		batch.Skipped = 3;

		await handler.Handle(Notification(batch, processed: 5, failed: 0), CancellationToken.None);

		Assert.That(store.Snapshot(), Is.Empty);
	}

	[Test]
	public async Task Completed_with_a_non_null_error_raises_warning_not_info()
	{
		var store = new UserNotificationStore();
		var cache = new FakeIconPackCache();
		cache.AddPack(_packId, "My Pack");
		var handler = CreateHandler(store, cache);

		var batch = Batch(IconImportBatchState.Completed);
		batch.Error = "Failed to extract icons.zip: corrupt archive";

		await handler.Handle(Notification(batch, processed: 5, failed: 0), CancellationToken.None);

		var notifications = store.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(1));
			Assert.That(notifications[0].Severity, Is.EqualTo(UserNotificationSeverity.Warning));
			Assert.That(notifications[0].Message, Is.EqualTo(batch.Error));
		});
	}

	[Test]
	public async Task CompletedWithErrors_raises_warning_with_processed_and_failed_counts()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store, new FakeIconPackCache());

		await handler.Handle(Notification(Batch(IconImportBatchState.CompletedWithErrors), processed: 3, failed: 2),
			CancellationToken.None);

		var notifications = store.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(1));
			Assert.That(notifications[0].Severity, Is.EqualTo(UserNotificationSeverity.Warning));
			Assert.That(notifications[0].Title, Is.EqualTo("3 imported, 2 failed into icons.zip"));
		});
	}

	[Test]
	public async Task Failed_raises_error_carrying_the_batch_error()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store, new FakeIconPackCache());

		var batch = Batch(IconImportBatchState.Failed);
		batch.Error = "Destination pack no longer exists";

		await handler.Handle(Notification(batch, processed: 0, failed: 0), CancellationToken.None);

		var notifications = store.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(1));
			Assert.That(notifications[0].Severity, Is.EqualTo(UserNotificationSeverity.Error));
			Assert.That(notifications[0].Message, Is.EqualTo(batch.Error));
		});
	}

	[Test]
	public async Task Cancelled_retires_the_running_entry_and_says_nothing()
	{
		var store = new UserNotificationStore();
		var cache = new FakeIconPackCache();
		cache.AddPack(_packId, "My Pack");
		var handler = CreateHandler(store, cache);
		var batch = Batch(IconImportBatchState.Processing);

		await handler.Handle(Notification(batch, processed: 2, failed: 0), CancellationToken.None);
		batch.State = IconImportBatchState.Cancelled;
		await handler.Handle(Notification(batch, processed: 4, failed: 0), CancellationToken.None);

		Assert.That(store.Snapshot(), Is.Empty);
	}

	[TestCase(IconImportBatchState.Processing)]
	[TestCase(IconImportBatchState.Completed)]
	[TestCase(IconImportBatchState.Cancelled)]
	public async Task A_silent_batch_stays_out_of_the_notification_center(IconImportBatchState state)
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store, new FakeIconPackCache());

		var batch = Batch(state);
		batch.Silent = true;

		await handler.Handle(Notification(batch, processed: 1, failed: 0), CancellationToken.None);

		Assert.That(store.Snapshot(), Is.Empty);
	}

	[Test]
	public async Task A_silent_batch_still_reports_a_failure()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store, new FakeIconPackCache());

		var batch = Batch(IconImportBatchState.Failed);
		batch.Silent = true;
		batch.Error = "Unsupported image";

		await handler.Handle(Notification(batch, processed: 0, failed: 1), CancellationToken.None);

		var notifications = store.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(1));
			Assert.That(notifications[0].Severity, Is.EqualTo(UserNotificationSeverity.Error));
			Assert.That(notifications[0].Message, Is.EqualTo(batch.Error));
		});
	}

	[Test]
	public async Task A_running_draft_carries_a_CancelKey_a_terminal_one_does_not()
	{
		var store = new UserNotificationStore();
		var cache = new FakeIconPackCache();
		cache.AddPack(_packId, "My Pack");
		var handler = CreateHandler(store, cache);
		var batch = Batch(IconImportBatchState.Processing);

		await handler.Handle(Notification(batch, processed: 1, failed: 0), CancellationToken.None);
		Assert.That(store.Snapshot()[0].CancelKey, Is.EqualTo(batch.Id.ToString()));

		batch.State = IconImportBatchState.CompletedWithErrors;
		await handler.Handle(Notification(batch, processed: 3, failed: 1), CancellationToken.None);
		Assert.That(store.Snapshot()[0].CancelKey, Is.Null, "a terminal entry carries no cancel handle");
	}

	[Test]
	public async Task Two_terminal_publishes_for_one_batch_yield_one_entry()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store, new FakeIconPackCache());
		var batch = Batch(IconImportBatchState.Processing);

		batch.State = IconImportBatchState.Failed;
		batch.Error = "boom";
		await handler.Handle(Notification(batch, processed: 0, failed: 1), CancellationToken.None);
		await handler.Handle(Notification(batch, processed: 0, failed: 1), CancellationToken.None);

		Assert.That(store.Snapshot(), Has.Count.EqualTo(1));
	}

	private static IconImportUserNotificationHandler CreateHandler(UserNotificationStore store, FakeIconPackCache cache)
		=> new(store, cache);

	private static IconImportBatchEntity Batch(IconImportBatchState state) => new()
	{
		Id = Guid.NewGuid(),
		PackId = _packId,
		State = state,
		SourceName = "icons.zip"
	};

	private static IconImportProgressNotification Notification(IconImportBatchEntity batch, int processed, int failed)
		=> new(batch, processed + failed, processed, failed);

	private sealed class FakeIconPackCache : IIconPackCache
	{
		private readonly Dictionary<Guid, IconPackEntity> _packs = new();

		public void AddPack(Guid id, string name) => _packs[id] = new IconPackEntity { Id = id, Name = name };

		public Task InitializeCache() => throw new NotSupportedException();

		public IconPackEntity? GetPackById(Guid id) => _packs.GetValueOrDefault(id);

		public List<IconPackEntity> GetAllPacks() => throw new NotSupportedException();

		public IconPackEntity? GetDefaultPack() => throw new NotSupportedException();

		public Task AddOrUpdatePack(IconPackEntity pack) => throw new NotSupportedException();

		public Task RemovePack(Guid id) => throw new NotSupportedException();

		public IconEntity? GetIconById(Guid iconId) => throw new NotSupportedException();

		public List<IconEntity> GetIconsByPackId(Guid packId) => throw new NotSupportedException();

		public List<IconEntity> GetIconsByBatchId(Guid batchId) => throw new NotSupportedException();

		public List<IconEntity> GetIconsByState(params IconProcessingState[] states)
			=> throw new NotSupportedException();

		public int GetIconCount(Guid packId) => throw new NotSupportedException();

		public IconEntity? FindBySourceContentHash(SourceContentHash hash, Guid? withinPackId = null) => null;

		public IconEntity? FindByMasterContentHash(MasterContentHash hash, Guid? withinPackId = null) => null;

		public List<IconEntity> GetIconsMissingMasterContentHash() => [];

		public Task AddIcons(Guid packId, IReadOnlyList<IconEntity> icons) => throw new NotSupportedException();

		public Task UpdateIcon(IconEntity icon) => throw new NotSupportedException();

		public Task RemoveIcon(Guid iconId) => throw new NotSupportedException();

		public Task RemoveIcons(Guid packId, IReadOnlyList<Guid> iconIds) => throw new NotSupportedException();

		public Task FlushPendingWrites() => throw new NotSupportedException();
	}
}
