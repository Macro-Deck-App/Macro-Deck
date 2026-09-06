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
		using var landed = new ManualResetEventSlim(false);

		var fixture = new PickerFixture();

		try
		{
			fixture.Session.Changed += (_, _) => landed.Set();

			fixture.NextCatalogCall().Completion.SetResult(Items("a", withArtwork: false));

			Assert.That(landed.Wait(WaitTimeout), Is.True, "the first answer never reached the view");

			landed.Reset();
			fixture.Session.DrainPatches();

			fixture.Type("B");

			var second = fixture.NextCatalogCall();

			Assert.That(second.Filter, Is.EqualTo("B"), "the session stopped asking for what the user typed");

			second.Completion.SetResult(Items("b", withArtwork: false));

			Assert.That(landed.Wait(WaitTimeout), Is.True, "the second answer never reached the view");

			Assert.That(RowIds(fixture.Session.BuildTree().Root),
				Is.EqualTo(Items("b", withArtwork: false).Select(item => _rowPrefix + item.Id)).AsCollection,
				"the session stopped updating after the first concurrent access");
		}
		finally
		{
			fixture.Session.DisposeAsync().AsTask().GetAwaiter().GetResult();
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

			return call.Completion.Task;
		}

		public PendingCall<ArtworkImageResult?> Next() => Take(_gate, _calls, "fetch a cover");
	}
}
