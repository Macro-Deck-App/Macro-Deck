using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;
using MacroDeckHost.Widgets.MusicPlayer;
using MacroDeck.Ui.Testing;
using Serilog;
using static MacroDeckHost.Tests.UnitTests.Widgets.Ui.UiSessionConcurrency;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

/// <summary>
/// The session's own contract: it follows the music player broadcast that already exists rather than
/// polling anything itself, it repaints through property patches, and it lets go of the broadcast when it
/// closes.
/// </summary>
[TestFixture]
public class MusicPlayerWidgetSessionTests
{
	[Test]
	public async Task A_broadcast_state_change_repaints_the_view_through_property_patches()
	{
		await using var fixture = new SessionFixture();

		fixture.Harness.Record(Playing(positionMs: 1_000, trackName: "Nannou"));
		await fixture.PublishAndSettleAsync();

		var patches = fixture.Session.DrainPatches();

		Assert.Multiple(() =>
		{
			Assert.That(patches, Is.Not.Empty);
			Assert.That(patches.SelectMany(patch => patch.Operations).Select(operation => operation.Op),
				Has.All.EqualTo(UiPatchOperations.SetProperties),
				"a state change must not reconcile the tree - see the issue's efficiency requirement");
		});
	}

	[Test]
	public async Task An_instance_list_change_refreshes_too_because_it_can_change_which_player_is_shown()
	{
		await using var fixture = new SessionFixture();

		fixture.Harness.Record(Playing(positionMs: 1_000, trackName: "Nannou"));
		fixture.Notifier.PublishInstancesChanged();
		await fixture.Session.WaitForRefreshAsync();

		Assert.That(fixture.Session.DrainPatches(), Is.Not.Empty);
	}

	[Test]
	public async Task A_playing_track_that_advanced_exactly_as_predicted_puts_nothing_on_the_wire()
	{
		await using var fixture = new SessionFixture();

		// The whole point of the reference: the reader is already showing the right second, so the poll
		// two seconds later has nothing to say.
		fixture.Harness.Advance(TimeSpan.FromSeconds(2));
		fixture.Harness.Record(Playing(positionMs: 44_000));
		await fixture.PublishAndSettleAsync();

		Assert.That(fixture.Session.DrainPatches(), Is.Empty);
	}

	[Test]
	public async Task A_closed_session_stops_following_the_broadcast()
	{
		var fixture = new SessionFixture();
		await fixture.DisposeAsync();

		fixture.Harness.Record(Playing(positionMs: 1_000, trackName: "Nannou"));
		await fixture.PublishAndSettleAsync();

		Assert.That(fixture.Session.DrainPatches(), Is.Empty);
	}

	[Test]
	public async Task A_saved_configuration_change_reaches_the_open_session()
	{
		await using var fixture = new SessionFixture();

		// The reported bug: a widget saved as "full cover" kept drawing the small one until something
		// reopened its session, because a shared widget session outlives an editor save.
		fixture.RenderSignals.RaiseDataChanged(new WidgetEntity
		{
			Id = Guid.Parse(WidgetId),
			Type = WidgetTypeIds.MusicPlayer,
			Data = """{"coverStyle":"full"}""",
		});
		await fixture.Session.WaitForRefreshAsync();

		var tree = fixture.Session.BuildTree();

		Assert.That(Ids(tree.Root), Does.Contain("musicPlayer.fullCover").And.Not.Contain("musicPlayer.smallCover"));
	}

	[Test]
	[CancelAfter(60_000)]
	public async Task A_refresh_landing_on_a_background_thread_while_a_client_dispatches_stays_a_healthy_session()
	{
		const int iterations = 200;
		const string racedTrack = "Nannou";
		const string tailTrack = "Ageispolis";

		await using var fixture = new SessionFixture();

		var failures = new ConcurrentBag<Exception>();
		var faults = 0;
		fixture.Session.Faulted += (_, _) => Interlocked.Increment(ref faults);

		var start = fixture.Session.BuildTree();

		// The write lands from RefreshLoopAsync's own continuation, which yields off the publishing thread
		// before it resolves - the ConfigureAwait(false) path the issue names.
		RunConcurrently(failures,
			() =>
			{
				for (var index = 0; index < iterations; index++)
				{
					fixture.Harness.Record(Playing(positionMs: 1_000 + index, trackName: racedTrack));
					fixture.Notifier.Publish(MusicPlayerTestHarness.InstanceId, new MusicPlayerStatePayload());
				}
			},
			() =>
			{
				for (var index = 0; index < iterations; index++)
				{
					fixture.Session.Dispatch(new UiEvent { NodeId = "musicPlayer", Name = "press" });
				}
			});

		AssertNoWorkerFaulted(failures);

		await fixture.Session.WaitForRefreshAsync();

		var patches = fixture.Session.DrainPatches();
		var replayed = start;

		foreach (var patch in patches)
		{
			var applied = UiTreeApplier.Apply(replayed, patch);

			Assert.That(applied.IsApplied, Is.True, applied.RejectionReason);

			replayed = applied.Tree;
		}

		Assert.Multiple(() =>
		{
			Assert.That(faults, Is.Zero);
			Assert.That(patches, Is.Not.Empty);
			Assert.That(Texts(fixture.Session.BuildTree().Root), Does.Contain(racedTrack));
		});

		// The deterministic tail: one further refresh, single-threaded, still costs exactly one patch.
		fixture.Harness.Record(Playing(positionMs: 5_000, trackName: tailTrack));
		await fixture.PublishAndSettleAsync();

		var tail = fixture.Session.DrainPatches();

		Assert.That(tail, Has.Count.EqualTo(1));
		Assert.That(Texts(fixture.Session.BuildTree().Root), Does.Contain(tailTrack));
	}

	/// <summary>Every string a node carries, so an assertion can say what the client is looking at without
	/// naming an id the view is free to compose differently.</summary>
	private static IEnumerable<string> Texts(UiNode node)
	{
		foreach (var value in node.Properties.Values)
		{
			if (value.ValueKind == JsonValueKind.String)
			{
				yield return value.GetString()!;
			}
		}

		foreach (var child in node.Children)
		{
			foreach (var text in Texts(child))
			{
				yield return text;
			}
		}

		if (node.Fallback is { } fallback)
		{
			foreach (var text in Texts(fallback))
			{
				yield return text;
			}
		}
	}

	private static IEnumerable<string> Ids(UiNode node)
	{
		yield return node.Id;

		foreach (var child in node.Children)
		{
			foreach (var id in Ids(child))
			{
				yield return id;
			}
		}
	}

	private static MusicPlayerStatePayload Playing(long positionMs, string trackName = "Windowlicker")
		=> new()
		{
			InstanceId = MusicPlayerTestHarness.InstanceId,
			IsConnected = true,
			IsPlaying = true,
			PlaybackState = "playing",
			TrackName = trackName,
			ArtistName = "Aphex Twin",
			PositionMs = positionMs,
			DurationMs = 215_000,
		};

	private const string WidgetId = "3f2b7c10-0000-0000-0000-000000000001";

	private sealed class SessionFixture : IAsyncDisposable
	{
		public SessionFixture()
		{
			Harness = new MusicPlayerTestHarness();
			Harness.Record(Playing(positionMs: 42_000));

			var config = new MusicPlayerWidgetData();
			var initial = Harness.ResolveAsync(config: config).GetAwaiter().GetResult();
			var state = new UiState<MusicPlayerViewState>(initial);
			var configState = new UiState<MusicPlayerWidgetData>(config);
			var icons = MusicPlayerWidgetIcons.EnsureRegistered(new UiResourceStore());

			var view = new UiView(new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
				MusicPlayerWidgetView.Build(state, configState, icons));

			Session = new MusicPlayerWidgetSession(view,
				state,
				configState,
				Harness.Resolver,
				Notifier,
				RenderSignals,
				WidgetId,
				new LoggerConfiguration().CreateLogger());

			// Drained once so a later assertion sees only what the refresh under test produced.
			Session.DrainPatches();
		}

		public MusicPlayerTestHarness Harness { get; }

		public MusicPlayerStateNotifier Notifier { get; } = new();

		public WidgetRenderSignals RenderSignals { get; } = new();

		public MusicPlayerWidgetSession Session { get; }

		public async Task PublishAndSettleAsync()
		{
			Notifier.Publish(MusicPlayerTestHarness.InstanceId, new MusicPlayerStatePayload());

			await Session.WaitForRefreshAsync();
		}

		public ValueTask DisposeAsync() => Session.DisposeAsync();
	}
}
