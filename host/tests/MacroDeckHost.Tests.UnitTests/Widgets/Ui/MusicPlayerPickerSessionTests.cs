using System.Collections;
using System.Collections.Concurrent;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Components;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Widgets.MusicPlayer;
using Serilog;
using static MacroDeckHost.Tests.UnitTests.Widgets.Ui.UiSessionConcurrency;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

/// <summary>
/// Regression coverage for the picker half of issue #830: the debounced search and the cover pass each run
/// on their own thread and each replace one of the session's cancellation sources, while a client's dispatch
/// and <c>DisposeAsync</c> do the same from theirs. The reported failure mode is a source cancelled on one
/// thread and disposed on another, which surfaced as an <see cref="ObjectDisposedException" /> out of a
/// session nobody could see fault. A pass's fault now reaches the session's own <c>Faulted</c> event, which
/// is what the tests here watch - not the process-wide unobserved-task event, where every other test's
/// abandoned task would land too.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MusicPlayerPickerSessionTests
{
	private const string _searchNodeId = "picker.search";

	private const string _listNodeId = "picker.results";

	private const string _rowPrefix = "picker.results.";

	[Test]
	[CancelAfter(60_000)]
	public async Task Disposing_the_picker_while_a_search_and_a_cover_are_in_flight_is_safe()
	{
		var failures = new ConcurrentBag<Exception>();
		var faults = 0;

		await using var fixture = new PickerFixture();

		fixture.Session.Faulted += (_, _) => Interlocked.Increment(ref faults);

		// The first load is the one the constructor starts, and its answer is what puts a cover fetch in
		// flight - so by the time the race begins the session has one of each outstanding.
		var first = fixture.NextCatalogCall();
		first.Completion.SetResult(Items("a", withArtwork: true));

		var cover = fixture.NextArtworkCall();

		fixture.Type("B");

		var second = fixture.NextCatalogCall();

		Assert.That(second.Filter, Is.EqualTo("B"));

		fixture.Session.DrainPatches();

		var disposeReturned = false;
		var emittedAfterDisposal = 0;

		fixture.Session.Changed += (_, _) =>
		{
			if (Volatile.Read(ref disposeReturned))
			{
				Interlocked.Increment(ref emittedAfterDisposal);
			}
		};

		RunConcurrently(failures,
			() =>
			{
				fixture.Session.DisposeAsync().AsTask().GetAwaiter().GetResult();
				Volatile.Write(ref disposeReturned, true);
			},
			() => second.Completion.SetResult(Items("b", withArtwork: true)),
			() => cover.Completion.SetResult(new ArtworkImageResult([1, 2, 3, 4], "image/webp", "\"a\"")));

		AssertNoWorkerFaulted(failures);

		// Both passes were released with their answers, so whatever either of them did with a source the
		// disposal had already taken has happened by the time the workers joined. A fault in there has
		// exactly one place to show up.
		Assert.Multiple(() =>
		{
			Assert.That(faults, Is.Zero, "a disposal race must not fault the session");
			Assert.That(second.Token.IsCancellationRequested, Is.True, "disposal has to cancel the search in flight");

			// The literal "nothing after disposal" promise, taken where it is decidable: whichever side of the
			// race won, nothing may reach a client once DisposeAsync has returned.
			Assert.That(emittedAfterDisposal, Is.Zero, "the session emitted after DisposeAsync returned");
		});

		// And the same promise once more with the race removed, so the assertion above cannot pass by the
		// disposal simply always winning.
		await using var settled = new PickerFixture();

		var pending = settled.NextCatalogCall();

		await settled.Session.DisposeAsync();

		settled.Session.DrainPatches();
		pending.Completion.SetResult(Items("late", withArtwork: false));

		Assert.That(settled.Session.DrainPatches(),
			Is.Empty,
			"a load landing after disposal must not put anything on the wire");
	}

	[Test]
	[CancelAfter(60_000)]
	public async Task A_fault_inside_a_background_pass_reaches_Faulted_instead_of_vanishing()
	{
		using var faulted = new ManualResetEventSlim(false);
		var reported = new ConcurrentBag<UiSessionFaultedEventArgs>();

		await using var fixture = new PickerFixture();

		fixture.Session.Faulted += (_, args) =>
		{
			reported.Add(args);
			faulted.Set();
		};

		var poison = new InvalidOperationException("the library's list fell over");

		// A library is plugin-owned, and so is the list it answers with: one that blows up when a row is read
		// takes down whichever pass reads it first, outside anything the session catches per call.
		fixture.NextCatalogCall().Completion.SetResult(new PoisonedList(poison));

		Assert.That(faulted.Wait(WaitTimeout), Is.True, "the pass's fault never reached the session's Faulted event");

		Assert.That(reported.Select(args => args.Exception), Is.EqualTo(new[] { poison }).AsCollection);
	}

	[Test]
	[CancelAfter(60_000)]
	public void A_second_search_replaces_the_first_one_s_rows_rather_than_dropping_it()
	{
		using var changed = new ManualResetEventSlim(false);

		var fixture = new PickerFixture();

		try
		{
			fixture.Session.Changed += (_, _) => changed.Set();

			fixture.NextCatalogCall().Completion.SetResult(Items("a", withArtwork: false));

			WaitForRows(fixture, changed, Rows("a"), "the first answer never reached the view");

			fixture.Session.DrainPatches();

			fixture.Type("B");

			var second = fixture.NextCatalogCall();

			Assert.That(second.Filter, Is.EqualTo("B"), "the session stopped asking for what the user typed");

			second.Completion.SetResult(Items("b", withArtwork: false));

			WaitForRows(fixture,
				changed,
				Rows("b"),
				"the second search's rows never replaced the first one's - the session stopped updating after " +
				"the first concurrent access");
		}
		finally
		{
			fixture.Session.DisposeAsync().AsTask().GetAwaiter().GetResult();
		}
	}

	[Test]
	[CancelAfter(60_000)]
	public async Task Covers_are_fetched_side_by_side_up_to_a_bound_and_each_one_appears_on_its_own()
	{
		await using var fixture = new PickerFixture();

		fixture.NextCatalogCall().Completion.SetResult(Covered("r", 25));

		var inFlight = Enumerable.Range(0, MusicPlayerPickerSession.MaxConcurrentCovers)
			.Select(_ => fixture.NextArtworkCall())
			.ToList();

		Assert.That(fixture.TryNextArtworkCall(out _),
			Is.False,
			"more covers were fetched at once than the session allows");

		var answered = inFlight[1];
		answered.Completion.SetResult(Image());

		WaitForCovers(fixture, [ItemFor(answered)], "a cover that arrived never reached the view on its own");

		Assert.That(fixture.NextArtworkCall().Filter,
			Is.Not.Null,
			"a finished fetch did not free its slot for the next cover");
	}

	[Test]
	[CancelAfter(60_000)]
	public async Task A_reveal_keeps_covers_in_flight_and_fetches_the_visible_rows_before_the_revealed_ones()
	{
		await using var fixture = new PickerFixture();

		fixture.NextCatalogCall().Completion.SetResult(Covered("r", 60));

		var inFlight = Enumerable.Range(0, MusicPlayerPickerSession.MaxConcurrentCovers)
			.Select(_ => fixture.NextArtworkCall())
			.ToList();

		fixture.Reveal(7);

		Assert.That(inFlight.Select(call => call.Token.IsCancellationRequested),
			Is.All.False,
			"a reveal cancelled covers that were already being fetched");

		var requested = inFlight.Select(call => call.Filter!).ToList();

		inFlight[0].Completion.SetResult(Image());

		var next = fixture.NextArtworkCall();
		requested.Add(next.Filter!);

		Assert.That(next.Filter, Is.EqualTo("r-art-07"), "a row below the screen was fetched before a visible one");

		var pending = new Queue<PendingCall<ArtworkImageResult?>>(inFlight.Skip(1).Append(next));
		var window = 7 + MusicPlayerPickerView.WindowGrowth;

		while (requested.Count < window)
		{
			pending.Dequeue().Completion.SetResult(Image());

			var call = fixture.NextArtworkCall();
			requested.Add(call.Filter!);
			pending.Enqueue(call);
		}

		while (pending.Count > 0)
		{
			pending.Dequeue().Completion.SetResult(Image());
		}

		var expected = Enumerable.Range(0, window).Select(index => $"r{index:00}").ToList();

		WaitForCovers(fixture, expected, "the grown window never showed every cover");

		Assert.Multiple(() =>
		{
			Assert.That(requested, Is.Unique, "a cover was fetched twice");
			Assert.That(requested,
				Is.EquivalentTo(Enumerable.Range(0, window).Select(index => $"r-art-{index:00}")));
		});
	}

	[Test]
	[CancelAfter(60_000)]
	public async Task A_new_search_frees_the_slots_of_the_old_list_for_its_own_covers()
	{
		await using var fixture = new PickerFixture();

		fixture.NextCatalogCall().Completion.SetResult(Covered("a", 25));

		var stale = Enumerable.Range(0, MusicPlayerPickerSession.MaxConcurrentCovers)
			.Select(_ => fixture.NextArtworkCall())
			.ToList();

		fixture.Type("B");
		fixture.NextCatalogCall().Completion.SetResult(Covered("b", 25));

		var fresh = Enumerable.Range(0, MusicPlayerPickerSession.MaxConcurrentCovers)
			.Select(_ => fixture.NextArtworkCall().Filter)
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(stale.Select(call => call.Token.IsCancellationRequested),
				Is.All.True,
				"covers of a list that was replaced kept their slots");
			Assert.That(fresh, Is.All.StartsWith("b-art-"), "the new list's covers did not get the freed slots");
		});
	}

	[Test]
	[CancelAfter(60_000)]
	public async Task A_cover_that_arrives_just_before_disposal_never_reaches_a_client()
	{
		var fixture = new PickerFixture();

		fixture.NextCatalogCall().Completion.SetResult(Items("a", withArtwork: true));

		var cover = fixture.NextArtworkCall();

		fixture.Session.DrainPatches();
		cover.Completion.SetResult(Image());

		var disposeReturned = false;
		var emittedAfterDisposal = 0;

		fixture.Session.Changed += (_, _) =>
		{
			if (Volatile.Read(ref disposeReturned))
			{
				Interlocked.Increment(ref emittedAfterDisposal);
			}
		};

		await fixture.Session.DisposeAsync();
		Volatile.Write(ref disposeReturned, true);

		await Task.Delay(MusicPlayerPickerSession.CoverPublishDelay * 5);

		Assert.Multiple(() =>
		{
			Assert.That(emittedAfterDisposal, Is.Zero, "the session emitted after DisposeAsync returned");
			Assert.That(fixture.Session.DrainPatches(), Is.Empty, "a cover was put on the wire after disposal");
		});
	}

	[Test]
	[CancelAfter(60_000)]
	public async Task A_cover_the_library_did_not_return_is_asked_for_again_for_a_new_list()
	{
		await using var fixture = new PickerFixture();

		fixture.NextCatalogCall().Completion.SetResult(Items("a", withArtwork: true));

		var failed = fixture.NextArtworkCall();
		failed.Completion.SetResult(null);

		fixture.Type("A");

		fixture.NextCatalogCall().Completion.SetResult(Items("a", withArtwork: true));

		Assert.That(fixture.NextArtworkCall().Filter,
			Is.EqualTo(failed.Filter),
			"a cover missing from the first list was never asked for again");
	}

	private static List<string> Rows(string prefix)
		=> Items(prefix, withArtwork: false).Select(item => _rowPrefix + item.Id).ToList();

	/// <summary>Waits for the tree to hold the rows the test asserts on rather than for one Changed signal -
	/// a pass raises that event for anything it writes, so a single signal says nothing about which answer
	/// has been applied. Bounded by the same timeout every other handshake uses and reports what the tree
	/// held last.</summary>
	private static void WaitForRows(
		PickerFixture fixture,
		ManualResetEventSlim changed,
		List<string> expected,
		string because)
	{
		var deadline = DateTime.UtcNow + WaitTimeout;

		while (true)
		{
			// Reset before the read, so a write that lands between the two still leaves the wait below armed.
			changed.Reset();

			var rows = RowIds(fixture.Session.BuildTree().Root);

			if (rows.SequenceEqual(expected, StringComparer.Ordinal))
			{
				return;
			}

			var remaining = deadline - DateTime.UtcNow;

			Assert.That(remaining > TimeSpan.Zero && changed.Wait(remaining),
				Is.True,
				$"{because}. Expected rows [{string.Join(", ", expected)}] but the tree last held " +
				$"[{string.Join(", ", rows)}]");
		}
	}

	private static IReadOnlyList<MusicPlayerCatalogItem> Items(string prefix, bool withArtwork)
		=>
		[
			new($"{prefix}1",
				$"{prefix} one",
				MusicPlayerCatalogItemKind.Track,
				ArtworkId: withArtwork ? $"{prefix}-art" : null),
			new($"{prefix}2", $"{prefix} two", MusicPlayerCatalogItemKind.Track),
		];

	private static List<MusicPlayerCatalogItem> Covered(string prefix, int count)
		=> Enumerable.Range(0, count)
			.Select(index => new MusicPlayerCatalogItem($"{prefix}{index:00}",
				$"{prefix} {index}",
				MusicPlayerCatalogItemKind.Track,
				ArtworkId: $"{prefix}-art-{index:00}"))
			.ToList();

	private static ArtworkImageResult Image() => new([1, 2, 3, 4], "image/webp", "\"cover\"");

	private static string ItemFor(PendingCall<ArtworkImageResult?> call)
		=> call.Filter!.Replace("-art-", string.Empty, StringComparison.Ordinal);

	private static void WaitForCovers(PickerFixture fixture, List<string> expected, string because)
	{
		var deadline = DateTime.UtcNow + WaitTimeout;

		while (true)
		{
			var covered = CoveredRows(fixture.Session.BuildTree().Root);

			if (covered.SetEquals(expected))
			{
				return;
			}

			Assert.That(DateTime.UtcNow < deadline,
				Is.True,
				$"{because}. Expected covers on [{string.Join(", ", expected)}] but the tree held them on " +
				$"[{string.Join(", ", covered.Order(StringComparer.Ordinal))}]");

			Thread.Sleep(10);
		}
	}

	private static HashSet<string> CoveredRows(UiNode root)
	{
		var covered = new HashSet<string>(StringComparer.Ordinal);

		Walk(root, null, covered);

		return covered;

		static void Walk(UiNode node, string? row, HashSet<string> into)
		{
			if (node.Id.StartsWith(_rowPrefix, StringComparison.Ordinal) &&
				node.Id.LastIndexOf('.') == _rowPrefix.Length - 1)
			{
				row = node.Id[_rowPrefix.Length..];
			}

			if (row is not null &&
				node.Properties.Values.Any(value => value.GetRawText().Contains(".pick.", StringComparison.Ordinal)))
			{
				into.Add(row);
			}

			foreach (var child in node.Children)
			{
				Walk(child, row, into);
			}
		}
	}

	/// <summary>Counts like a list of one covered row and throws the moment that row is read.</summary>
	private sealed class PoisonedList : IReadOnlyList<MusicPlayerCatalogItem>
	{
		private readonly Exception _exception;

		public PoisonedList(Exception exception) => _exception = exception;

		public int Count => 1;

		public MusicPlayerCatalogItem this[int index] => throw _exception;

		public IEnumerator<MusicPlayerCatalogItem> GetEnumerator() => throw _exception;

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}

	private static List<string> RowIds(UiNode root)
	{
		var ids = new List<string>();

		Walk(root, ids);

		return ids;

		static void Walk(UiNode node, List<string> into)
		{
			// A row is a direct child of the results list, so its id is the list's plus the item's own.
			if (node.Id.StartsWith(_rowPrefix, StringComparison.Ordinal) &&
				node.Id.LastIndexOf('.') == _rowPrefix.Length - 1)
			{
				into.Add(node.Id);
			}

			foreach (var child in node.Children)
			{
				Walk(child, into);
			}
		}
	}

	/// <summary>Blocks until the session made the next call of this kind, under the same bounded wait every
	/// other handshake uses - a fake that slept for it would be timing rather than waiting.</summary>
	private static T Take<T>(object gate, Queue<T> calls, string what)
	{
		lock (gate)
		{
			while (calls.Count == 0)
			{
				Assert.That(Monitor.Wait(gate, WaitTimeout), Is.True, $"the session never came to {what}");
			}

			return calls.Dequeue();
		}
	}

	private sealed class PickerFixture : IAsyncDisposable
	{
		private readonly FakeCatalogPlayer _player = new();
		private readonly FakeArtworkService _artwork = new();

		public PickerFixture()
			=> Session = new MusicPlayerPickerSession(
				new UiSurface { Kind = UiSurfaceKinds.Dialog, SessionMode = UiSessionModes.Exclusive },
				new FakeCatalogRegistry(_player),
				_artwork,
				new UiResourceStore(),
				"spotify.1",
				MusicPlayerCatalogItemKind.Track,
				new LoggerConfiguration().CreateLogger());

		public MusicPlayerPickerSession Session { get; }

		public void Type(string text)
			=> Session.Dispatch(new UiEvent
			{
				NodeId = _searchNodeId,
				Name = UiComponentEvents.Change,
				Data = UiCanonicalJson.ToElement(text),
			});

		/// <summary>The next catalog read the session performs, waited for rather than slept for - the
		/// session's own debounce is what decides when it happens.</summary>
		public PendingCall<IReadOnlyList<MusicPlayerCatalogItem>> NextCatalogCall() => _player.Next();

		public PendingCall<ArtworkImageResult?> NextArtworkCall() => _artwork.Next();

		public bool TryNextArtworkCall(out PendingCall<ArtworkImageResult?>? call) => _artwork.TryNext(out call);

		public void Reveal(int index)
			=> Session.Dispatch(new UiEvent
			{
				NodeId = _listNodeId,
				Name = UiComponentEvents.Reveal,
				Data = UiCanonicalJson.ToElement(index),
			});

		public ValueTask DisposeAsync() => Session.DisposeAsync();
	}

	/// <summary>One call the session made: what it asked for, the token it was handed, and the answer the
	/// test decides to give it and when.</summary>
	internal sealed class PendingCall<T>
	{
		public PendingCall(string? filter, CancellationToken token)
		{
			Filter = filter;
			Token = token;
		}

		public string? Filter { get; }

		public CancellationToken Token { get; }

		public TaskCompletionSource<T> Completion { get; } = new();
	}

	private sealed class FakeCatalogRegistry : IMusicPlayerRegistry
	{
		private readonly IMusicPlayer _player;

		public FakeCatalogRegistry(IMusicPlayer player) => _player = player;

		public IMusicPlayer? DefaultPlayer => _player;

		public IReadOnlyList<MusicPlayerInstanceDescriptor> GetInstances() => [];

		public IMusicPlayer? GetPlayer(string instanceId) => _player;
	}

	private sealed class FakeCatalogPlayer : IMusicPlayer, IMusicPlayerCatalogProvider
	{
		private readonly Queue<PendingCall<IReadOnlyList<MusicPlayerCatalogItem>>> _calls = new();
		private readonly object _gate = new();

		public Task<IReadOnlyList<MusicPlayerCatalogItem>> GetCatalogAsync(
			string instanceId,
			MusicPlayerCatalogItemKind kind,
			string? filter,
			CancellationToken cancellationToken)
		{
			var call = new PendingCall<IReadOnlyList<MusicPlayerCatalogItem>>(filter, cancellationToken);

			lock (_gate)
			{
				_calls.Enqueue(call);
				Monitor.PulseAll(_gate);
			}

			return call.Completion.Task;
		}

		public PendingCall<IReadOnlyList<MusicPlayerCatalogItem>> Next() => Take(_gate, _calls, "read the catalog");

		public Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default)
			=> Task.FromResult(MusicPlayerState.Disconnected);

		public Task<MusicPlayerArtwork?> GetArtworkAsync(string artworkId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult<MusicPlayerArtwork?>(null);

		public Task PlayAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task TogglePlayPauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task NextAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task PreviousAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
	}

	private sealed class FakeArtworkService : IMusicPlayerArtworkService
	{
		private readonly Queue<PendingCall<ArtworkImageResult?>> _calls = new();
		private readonly object _gate = new();

		public string GetETag(string artworkId, int? size) => $"\"{artworkId}\"";

		public Task<ArtworkImageResult?> GetImage(
			string instanceId,
			string artworkId,
			int? size,
			CancellationToken cancellationToken)
		{
			var call = new PendingCall<ArtworkImageResult?>(artworkId, cancellationToken);

			lock (_gate)
			{
				_calls.Enqueue(call);
				Monitor.PulseAll(_gate);
			}

			return call.Completion.Task.WaitAsync(cancellationToken);
		}

		public PendingCall<ArtworkImageResult?> Next() => Take(_gate, _calls, "fetch a cover");

		public bool TryNext(out PendingCall<ArtworkImageResult?>? call)
		{
			lock (_gate)
			{
				if (_calls.Count == 0)
				{
					Monitor.Wait(_gate, TimeSpan.FromMilliseconds(300));
				}

				return _calls.TryDequeue(out call);
			}
		}
	}
}
