using System.Text.Json;
using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Mcp;

public class McpJournalTests
{
	private ManualTimeProvider _time = null!;
	private RecordingSecretLedger _secrets = null!;
	private JsonStateStore _store = null!;
	private McpJournal _journal = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new ManualTimeProvider();
		_secrets = new RecordingSecretLedger();
		_store = new JsonStateStore();
		_journal = new McpJournal(_store, _secrets, _time);
	}

	[Test]
	public async Task A_delete_batch_stays_undoable_after_many_more_batches_that_delete_nothing()
	{
		var delete = await Append(McpJournalOperation.Deleted);

		for (var batch = 0; batch < McpJournal.Capacity * 3; batch++)
		{
			await Append(McpJournalOperation.Updated);
		}

		Assert.That(_journal.Find(delete.BatchId), Is.Not.Null);
	}

	[Test]
	public async Task Only_the_last_batches_that_delete_nothing_are_kept()
	{
		var first = await Append(McpJournalOperation.Created);
		var kept = new List<Guid>();
		for (var batch = 0; batch < McpJournal.Capacity; batch++)
		{
			kept.Add((await Append(McpJournalOperation.Updated)).BatchId);
		}

		await Assert.MultipleAsync(() =>
		{
			Assert.That(_journal.Find(first.BatchId), Is.Null);
			Assert.That(_journal.Batches().Select(batch => batch.BatchId), Is.EqualTo(kept));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task The_oldest_delete_batch_leaves_once_the_delete_budget_is_full()
	{
		var oldest = await Append(McpJournalOperation.Deleted);
		var next = await Append(McpJournalOperation.Deleted);
		for (var batch = 1; batch < McpJournal.DeleteCapacity; batch++)
		{
			await Append(McpJournalOperation.Deleted);
		}

		await Assert.MultipleAsync(() =>
		{
			Assert.That(_journal.Find(oldest.BatchId), Is.Null);
			Assert.That(_journal.Find(next.BatchId), Is.Not.Null);
			Assert.That(_secrets.Scrubs, Is.EqualTo(1));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_delete_batch_can_no_longer_be_undone_once_it_is_older_than_the_retention()
	{
		var old = await Append(McpJournalOperation.Deleted);
		_time.Advance(TimeSpan.FromDays(10));
		var younger = await Append(McpJournalOperation.Deleted);

		_time.Advance(McpJournal.DeleteRetention - TimeSpan.FromDays(5));
		var readBeforeWrite = _journal.Find(old.BatchId);
		await Append(McpJournalOperation.Updated);

		await Assert.MultipleAsync(() =>
		{
			Assert.That(readBeforeWrite, Is.Null);
			Assert.That(_journal.Batches().Select(batch => batch.BatchId), Does.Not.Contain(old.BatchId));
			Assert.That(_journal.Find(younger.BatchId), Is.Not.Null);
			Assert.That(_secrets.Scrubs, Is.EqualTo(1));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task An_expired_delete_batch_leaves_the_stored_journal_without_a_later_batch()
	{
		await Append(McpJournalOperation.Deleted);
		var ordinary = await Append(McpJournalOperation.Updated);
		_time.Advance(McpJournal.DeleteRetention + TimeSpan.FromDays(1));

		_journal.RemoveExpired();

		var stored = _store.Read<McpJournalDocument>(IMcpStateStore.JournalDocument)!;
		Assert.That(stored.Batches.Select(batch => batch.BatchId), Is.EqualTo(new[] { ordinary.BatchId }));
	}

	[Test]
	public async Task Secrets_are_not_scrubbed_while_nothing_leaves_the_history()
	{
		await Append(McpJournalOperation.Deleted);
		for (var batch = 0; batch < McpJournal.Capacity; batch++)
		{
			await Append(McpJournalOperation.Updated);
		}

		Assert.That(_secrets.Scrubs, Is.Zero);
	}

	private async Task<McpJournalBatch> Append(McpJournalOperation operation)
	{
		var batch = new McpJournalBatch
		{
			BatchId = Guid.NewGuid(),
			At = _time.GetUtcNow().UtcDateTime,
			TokenId = Guid.Empty,
			Summary = operation.ToString(),
			Items = [new McpJournalItem { Entity = McpEntities.Widget, Id = Guid.NewGuid(), Operation = operation }]
		};
		await _journal.Append(batch);
		return batch;
	}

	private sealed class JsonStateStore : IMcpStateStore
	{
		private readonly Dictionary<string, string> _documents = [];

		public T? Read<T>(string document)
			where T : class
		{
			return _documents.TryGetValue(document, out var json) ? JsonSerializer.Deserialize<T>(json) : null;
		}

		public void Write<T>(string document, T value)
			where T : class
		{
			_documents[document] = JsonSerializer.Serialize(value);
		}

		public void Delete(string document)
		{
			_documents.Remove(document);
		}
	}

	private sealed class RecordingSecretLedger : IMcpSecretLedger
	{
		public int Scrubs { get; private set; }

		public void Grant(Guid tokenId, Guid secretId)
		{
		}

		public bool IsGranted(Guid tokenId, Guid secretId)
		{
			return false;
		}

		public void Defer(IEnumerable<Guid> secretIds)
		{
		}

		public Task ForgetToken(Guid tokenId, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task ScrubDeferred(CancellationToken cancellationToken = default)
		{
			Scrubs++;
			return Task.CompletedTask;
		}
	}
}
