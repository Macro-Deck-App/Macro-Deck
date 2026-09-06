using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Infrastructure.Notifications;

namespace MacroDeckHost.Tests.UnitTests.Notifications;

public class UserNotificationStoreTests
{
	[Test]
	public void Raise_Assigns_Monotonic_Sequences()
	{
		var store = new UserNotificationStore(10);

		var first = store.Raise(Draft("first"));
		var second = store.Raise(Draft("second"));

		Assert.Multiple(() =>
		{
			Assert.That(first!.Sequence, Is.EqualTo(1));
			Assert.That(second!.Sequence, Is.EqualTo(2));
		});
	}

	[Test]
	public void Capacity_Trim_Drops_The_Oldest_And_Forgets_Its_Dedupe_Key()
	{
		var store = new UserNotificationStore(3);

		store.Raise(Draft("one", dedupeKey: "key-1"));
		store.Raise(Draft("two", dedupeKey: "key-2"));
		store.Raise(Draft("three", dedupeKey: "key-3"));
		store.Raise(Draft("four", dedupeKey: "key-4"));

		var titlesAfterTrim = store.Snapshot().Select(e => e.Title).ToList();

		string[] expectedAfterTrim = ["four", "three", "two"];
		Assert.That(titlesAfterTrim, Is.EqualTo(expectedAfterTrim));

		var reraised = store.Raise(Draft("one-again", dedupeKey: "key-1"));

		string[] expectedAfterReraise = ["one-again", "four", "three"];
		Assert.Multiple(() =>
		{
			Assert.That(store.Snapshot(), Has.Count.EqualTo(3));
			Assert.That(store.Snapshot().Select(e => e.Title), Is.EqualTo(expectedAfterReraise));
			Assert.That(reraised!.Sequence, Is.EqualTo(5));
		});
	}

	[Test]
	public void Repeat_Dedupe_Key_Reinserts_At_The_Front_Keeps_Id_And_Bumps_Sequence_And_Timestamp()
	{
		var store = new UserNotificationStore(10);

		var original = store.Raise(Draft("integration offline", dedupeKey: "integration-x-offline"))!;
		store.Raise(Draft("something else"));

		var replacement = store.Raise(Draft("integration offline (retry)", dedupeKey: "integration-x-offline"))!;

		Assert.Multiple(() =>
		{
			Assert.That(replacement.Id, Is.EqualTo(original.Id));
			Assert.That(replacement.Sequence, Is.GreaterThan(original.Sequence));
			Assert.That(store.Snapshot()[0].Id, Is.EqualTo(original.Id));
			Assert.That(store.Snapshot(), Has.Count.EqualTo(2));
		});
	}

	[Test]
	public void CancelKey_Survives_Both_The_Create_And_The_Dedupe_Replace_Path()
	{
		var store = new UserNotificationStore(10);

		var created = store.Raise(Draft("importing") with
		{
			DedupeKey = "icon-import:batch-1",
			CancelKey = "batch-1"
		})!;
		Assert.That(created.CancelKey, Is.EqualTo("batch-1"));

		var replaced = store.Raise(Draft("imported") with
		{
			DedupeKey = "icon-import:batch-1",
			CancelKey = null
		})!;
		Assert.Multiple(() =>
		{
			Assert.That(replaced.Id, Is.EqualTo(created.Id), "same dedupe key must replace, not append");
			Assert.That(replaced.CancelKey, Is.Null, "the terminal replacement carries no cancel handle");
		});
	}

	[Test]
	public void DismissByKey_Removes_The_Entry_And_The_Mapping()
	{
		var store = new UserNotificationStore(10);
		store.Raise(Draft("one", dedupeKey: "key-1"));

		var removed = store.DismissByKey("key-1");

		Assert.Multiple(() =>
		{
			Assert.That(removed, Is.True);
			Assert.That(store.Snapshot(), Is.Empty);
			var reraised = store.Raise(Draft("one-again", dedupeKey: "key-1"));
			Assert.That(store.Snapshot().Select(e => e.Id), Is.EqualTo(new[] { reraised!.Id }));
		});
	}

	[Test]
	public void Every_Mutation_Returns_False_On_A_No_Op()
	{
		var store = new UserNotificationStore(10);

		Assert.Multiple(() =>
		{
			Assert.That(store.Dismiss("unknown-id"), Is.False);
			Assert.That(store.DismissByKey("unknown-key"), Is.False);
			Assert.That(store.Retire("unknown-key"), Is.False);
			Assert.That(store.DismissAll(), Is.False);
		});
	}

	[Test]
	public void Changed_Fires_Once_Per_Real_Change()
	{
		var store = new UserNotificationStore(10);
		var raiseCount = 0;
		store.Changed += () => raiseCount++;

		var entry = store.Raise(Draft("one"))!;
		Assert.That(raiseCount, Is.EqualTo(1));

		store.Dismiss(entry.Id);
		Assert.That(raiseCount, Is.EqualTo(2));
		// A no-op mutation must not fire Changed again.
		store.Dismiss(entry.Id);
		Assert.That(raiseCount, Is.EqualTo(2));
	}

	[Test]
	public void A_Throwing_Subscriber_Does_Not_Propagate_Out_Of_Raise()
	{
		var store = new UserNotificationStore(10);
		store.Changed += () => throw new InvalidOperationException("subscriber blew up");

		UserNotification? entry = null;
		Assert.That(() => entry = store.Raise(Draft("one")), Throws.Nothing);
		Assert.That(store.Snapshot().Single().Id, Is.EqualTo(entry!.Id));
	}

	[Test]
	public void Null_Or_Blank_Title_Returns_Null_And_Stores_Nothing()
	{
		var store = new UserNotificationStore(10);

		Assert.Multiple(() =>
		{
			Assert.That(store.Raise(Draft(null!)), Is.Null);
			Assert.That(store.Raise(Draft("")), Is.Null);
			Assert.That(store.Raise(Draft("   ")), Is.Null);
			Assert.That(store.Raise(null!), Is.Null);
		});

		Assert.That(store.Snapshot(), Is.Empty);
	}

	[Test]
	public void Title_And_Message_Are_Clamped()
	{
		var store = new UserNotificationStore(10);
		var longTitle = new string('a', 250);
		var longMessage = new string('b', 1200);

		var entry = store.Raise(Draft(longTitle, message: longMessage))!;

		Assert.Multiple(() =>
		{
			Assert.That(entry.Title, Has.Length.EqualTo(200));
			Assert.That(entry.Message, Has.Length.EqualTo(1000));
		});
	}

	[Test]
	public void Raising_Credential_Material_Stores_The_Redacted_Form()
	{
		var store = new UserNotificationStore(10);
		const string raw = "Authorization: Bearer eyJabc.def.ghi";
		var expected = LogRedactor.Redact(raw);

		var entry = store.Raise(Draft(raw, message: raw))!;

		Assert.Multiple(() =>
		{
			Assert.That(entry.Title, Is.EqualTo(expected));
			Assert.That(entry.Message, Is.EqualTo(expected));
			Assert.That(entry.Title, Does.Not.Contain("eyJabc.def.ghi"));
		});
	}

	[Test]
	public void A_Draft_With_Three_Actions_Keeps_All_Three_And_Mirrors_The_First_Into_Action()
	{
		var store = new UserNotificationStore(10);
		UserNotificationAction[] actions =
		[
			new(UserNotificationActionKind.OpenUpdateDetails, "1.2.3"),
			new(UserNotificationActionKind.InstallUpdate, "1.2.3"),
			new(UserNotificationActionKind.DismissNotification, null)
		];

		var entry = store.Raise(Draft("multi-action") with { Actions = actions })!;

		Assert.Multiple(() =>
		{
			Assert.That(entry.Actions, Is.EqualTo(actions), "the regression case: an old single-action consumer");
			Assert.That(entry.Action, Is.EqualTo(actions[0]));
		});
	}

	[Test]
	public void A_Draft_With_Only_The_Singular_Action_Yields_A_One_Element_Actions_List()
	{
		var store = new UserNotificationStore(10);
		var action = new UserNotificationAction(UserNotificationActionKind.OpenUpdateSettings, null);

		var entry = store.Raise(Draft("single-action") with { Action = action })!;

		Assert.Multiple(() =>
		{
			Assert.That(entry.Action, Is.EqualTo(action));
			Assert.That(entry.Actions, Is.EqualTo(new[] { action }));
		});
	}

	[Test]
	public void A_Draft_With_More_Than_Three_Actions_Is_Capped_At_Three()
	{
		var store = new UserNotificationStore(10);
		UserNotificationAction[] actions =
		[
			new(UserNotificationActionKind.OpenUpdateDetails, null),
			new(UserNotificationActionKind.InstallUpdate, null),
			new(UserNotificationActionKind.DismissNotification, null),
			new(UserNotificationActionKind.OpenUpdateSettings, null)
		];

		var entry = store.Raise(Draft("too-many-actions") with { Actions = actions })!;

		Assert.That(entry.Actions, Has.Count.EqualTo(3));
	}

	[Test]
	public void UpdateProgress_Replaces_Only_Progress_At_The_Same_Index()
	{
		var store = new UserNotificationStore(10);
		store.Raise(Draft("older"));
		var entry = store.Raise(DraftWithProgress("importing", "key-1", new UserNotificationProgress(1, 10)))!;

		var updated = store.UpdateProgress("key-1", new UserNotificationProgress(5, 10));

		var snapshot = store.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(updated, Is.True);
			Assert.That(snapshot[0].Id, Is.EqualTo(entry.Id));
			Assert.That(snapshot[0].Sequence, Is.EqualTo(entry.Sequence));
			Assert.That(snapshot[0].Timestamp, Is.EqualTo(entry.Timestamp));
			Assert.That(snapshot[0].Progress, Is.EqualTo(new UserNotificationProgress(5, 10)));
		});
	}

	[Test]
	public void UpdateProgress_Preserves_Position_Of_An_Older_Entry()
	{
		var store = new UserNotificationStore(10);
		var running = store.Raise(DraftWithProgress("importing", "key-1", new UserNotificationProgress(1, 10)))!;
		store.Raise(Draft("newer"));

		store.UpdateProgress("key-1", new UserNotificationProgress(2, 10));

		var snapshot = store.Snapshot();
		Assert.Multiple(() =>
		{
			// The running entry stays second - a progress tick must not re-sort it to the front.
			Assert.That(snapshot[1].Id, Is.EqualTo(running.Id));
			Assert.That(snapshot[1].Sequence, Is.EqualTo(running.Sequence));
		});
	}

	[Test]
	public void UpdateProgress_Returns_False_For_An_Unknown_Key()
	{
		var store = new UserNotificationStore(10);

		Assert.That(store.UpdateProgress("no-such-key", new UserNotificationProgress(1, 10)), Is.False);
	}

	[Test]
	public void UpdateProgress_Returns_False_And_Fires_No_Changed_When_The_Value_Is_Unchanged()
	{
		var store = new UserNotificationStore(10);
		store.Raise(DraftWithProgress("importing", "key-1", new UserNotificationProgress(1, 10)));
		var raiseCount = 0;
		store.Changed += () => raiseCount++;

		var updated = store.UpdateProgress("key-1", new UserNotificationProgress(1, 10));

		Assert.Multiple(() =>
		{
			Assert.That(updated, Is.False);
			Assert.That(raiseCount, Is.EqualTo(0));
		});
	}

	[Test]
	public void UpdateProgress_Fires_Changed_Once_When_It_Changes_The_Value()
	{
		var store = new UserNotificationStore(10);
		store.Raise(DraftWithProgress("importing", "key-1", new UserNotificationProgress(1, 10)));
		var raiseCount = 0;
		store.Changed += () => raiseCount++;

		store.UpdateProgress("key-1", new UserNotificationProgress(2, 10));

		Assert.That(raiseCount, Is.EqualTo(1));
	}

	[Test]
	public void UpdateProgress_Does_Not_Reintroduce_Progress_Once_It_Has_Been_Cleared()
	{
		var store = new UserNotificationStore(10);
		store.Raise(DraftWithProgress("importing", "key-1", new UserNotificationProgress(1, 10)));
		store.Raise(Draft("done", dedupeKey: "key-1"));

		var updated = store.UpdateProgress("key-1", new UserNotificationProgress(9, 10));

		Assert.Multiple(() =>
		{
			Assert.That(updated, Is.False);
			Assert.That(store.Snapshot()[0].Progress, Is.Null);
		});
	}

	[Test]
	public void Parallel_Raises_Yield_N_Entries_With_N_Distinct_Sequences()
	{
		var store = new UserNotificationStore(500);
		const int n = 200;

		Parallel.For(0, n, i => store.Raise(Draft($"entry-{i}")));

		var sequences = store.Snapshot().Select(e => e.Sequence).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(store.Snapshot(), Has.Count.EqualTo(n));
			Assert.That(sequences.Distinct().Count(), Is.EqualTo(n));
		});
	}

	private static UserNotificationDraft Draft(string title, string? message = null, string? dedupeKey = null)
		=> new()
		{
			Severity = UserNotificationSeverity.Info,
			Kind = UserNotificationKind.General,
			Title = title,
			Message = message,
			DedupeKey = dedupeKey
		};

	private static UserNotificationDraft DraftWithProgress(string title,
		string dedupeKey,
		UserNotificationProgress progress)
		=> Draft(title, dedupeKey: dedupeKey) with { Progress = progress };

	[Test]
	public void A_Running_Entry_Is_Not_The_Users_To_Dismiss()
	{
		var store = new UserNotificationStore(10);
		var running = store.Raise(DraftWithProgress("importing", "key-1", new UserNotificationProgress(1, 10)))!;
		store.Raise(Draft("something finished"));

		Assert.Multiple(() =>
		{
			Assert.That(store.Dismiss(running.Id), Is.False, "a running entry must survive Dismiss");
			Assert.That(store.DismissAll(), Is.True);
			string[] expected = [running.Id];
			Assert.That(store.Snapshot().Select(e => e.Id), Is.EqualTo(expected));
		});
	}

	[Test]
	public void DismissAll_Keeps_The_Dedupe_Mapping_Of_What_It_Kept()
	{
		var store = new UserNotificationStore(10);
		var running = store.Raise(DraftWithProgress("importing", "key-1", new UserNotificationProgress(1, 10)))!;
		store.Raise(Draft("noise"));

		store.DismissAll();

		Assert.Multiple(() =>
		{
			Assert.That(store.UpdateProgress("key-1", new UserNotificationProgress(2, 10)), Is.True);
			string[] expected = [running.Id];
			Assert.That(store.Snapshot().Select(e => e.Id), Is.EqualTo(expected));
		});
	}

	[Test]
	public void DismissAll_Reports_False_When_Only_Running_Entries_Remain()
	{
		var store = new UserNotificationStore(10);
		store.Raise(DraftWithProgress("importing", "key-1", new UserNotificationProgress(1, 10)));

		Assert.That(store.DismissAll(), Is.False);
	}

	[Test]
	public void Retire_Removes_The_Entry_And_Keeps_A_Straggler_From_Replacing_It()
	{
		var store = new UserNotificationStore(10);
		store.Raise(DraftWithProgress("importing", "key-1", new UserNotificationProgress(1, 10)));

		Assert.Multiple(() =>
		{
			Assert.That(store.Retire("key-1"), Is.True);
			Assert.That(store.Snapshot(), Is.Empty);
			Assert.That(store.RaiseIfAbsent(DraftWithProgress("importing",
					"key-1",
					new UserNotificationProgress(2, 10))),
				Is.Null);
			Assert.That(store.Snapshot(), Is.Empty);
		});
	}

	[Test]
	public void An_Explicit_Raise_Puts_A_Retired_Key_Back_Into_Circulation()
	{
		var store = new UserNotificationStore(10);
		store.Raise(DraftWithProgress("importing", "key-1", new UserNotificationProgress(1, 10)));
		store.Retire("key-1");

		// The producer deliberately speaking again - an outcome after the running entry went away.
		var raised = store.Raise(Draft("import failed", dedupeKey: "key-1"));

		Assert.Multiple(() =>
		{
			Assert.That(raised, Is.Not.Null);
			string[] expected = ["import failed"];
			Assert.That(store.Snapshot().Select(e => e.Title), Is.EqualTo(expected));
			Assert.That(store.RaiseIfAbsent(Draft("again", dedupeKey: "key-1")), Is.Null);
		});
	}

	[Test]
	public void Retired_Keys_Do_Not_Grow_Without_Bound()
	{
		var store = new UserNotificationStore(2);

		for (var i = 0; i < 5; i++)
		{
			store.Raise(Draft($"entry {i}", dedupeKey: $"key-{i}"));
			store.Retire($"key-{i}");
		}

		Assert.That(store.RaiseIfAbsent(Draft("key-0 again", dedupeKey: "key-0")), Is.Not.Null);
	}

	[Test]
	public void Retire_Reserves_The_Key_Even_When_There_Was_Nothing_To_Remove()
	{
		var store = new UserNotificationStore(10);

		Assert.That(store.Retire("key-1"), Is.False, "there was no entry to remove");

		var straggler = store.RaiseIfAbsent(DraftWithProgress("importing",
			"key-1",
			new UserNotificationProgress(1, 10)));

		Assert.Multiple(() =>
		{
			Assert.That(straggler, Is.Null);
			Assert.That(store.Snapshot(), Is.Empty);
		});
	}

	[Test]
	public void An_Aged_Out_Queue_Slot_Does_Not_Cancel_A_Newer_Reservation()
	{
		var store = new UserNotificationStore(3);

		store.Retire("key-1");
		store.Raise(Draft("spoke again", dedupeKey: "key-1"));
		store.Retire("key-1");

		store.Retire("key-2");
		store.Retire("key-3");

		Assert.That(store.RaiseIfAbsent(Draft("straggler", dedupeKey: "key-1")), Is.Null);
	}
}
