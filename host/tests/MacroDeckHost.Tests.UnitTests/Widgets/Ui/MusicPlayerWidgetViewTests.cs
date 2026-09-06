using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Model.References;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Testing;
using MacroDeck.Ui.Components;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Widgets;
using MacroDeckHost.Widgets.MusicPlayer;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

/// <summary>
/// What issue #749 asks the migrated Music Player to be: one tree a generic renderer draws, whose cover
/// travels as a resource handle, whose timeline travels as a reference rather than a number pushed every
/// second, and which is still four visibly different things in the four states it can be in.
/// </summary>
[TestFixture]
public class MusicPlayerWidgetViewTests
{
	private static readonly DateTimeOffset _anchor = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

	[Test]
	public void The_tree_takes_no_size_and_every_length_is_a_basis_fraction()
	{
		var host = Render(Playing());
		var lengthKeys = new[] { "mainSize", "size", "minSize", "gap", "padding", "thickness" };

		Assert.Multiple(() =>
		{
			foreach (var node in Walk(host.Root))
			{
				foreach (var key in lengthKeys)
				{
					if (node.Property(key) is not { } value)
					{
						continue;
					}

					Assert.That(value.ValueKind,
						Is.EqualTo(JsonValueKind.Object),
						$"'{node.Id}'.{key} must be a {{basis,...}} length, never a raw number.");
					Assert.That(value.GetProperty("basis").GetDouble(),
						Is.LessThan(3),
						$"'{node.Id}'.{key} looks like a pixel value rather than a widget-basis fraction.");
				}
			}
		});
	}

	[Test]
	public void No_node_claims_a_press_so_the_deck_tile_keeps_running_the_widgets_flows()
	{
		// The press that runs this widget's flows is the tile's, exactly as it was before the migration.
		// A node that declared one would take the gesture away from the tile and leave the flows unrun.
		var host = Render(Playing());

		Assert.That(Walk(host.Root).Where(node => node.Property("events") is not null),
			Is.Empty,
			"a Music Player node declaring an event would silently stop the tile's press from firing");
	}

	[Test]
	public void The_timeline_carries_a_progress_reference_rather_than_a_formatted_time()
	{
		var host = Render(Playing());

		var bar = Node(host, "progress");
		var elapsed = Node(host, "elapsed");
		var duration = Node(host, "duration");

		Assert.Multiple(() =>
		{
			Assert.That(bar.Type, Is.EqualTo(UiMacroDeckComponents.ProgressBar));
			Assert.That(bar.Property("value")!.Value.GetProperty("$progress").GetProperty("positionMs").GetInt64(),
				Is.EqualTo(42_000));
			// Absent means normal speed, so a playing track spends no key on saying so.
			Assert.That(bar.Property("value")!.Value.GetProperty("$progress").TryGetProperty("rate", out _), Is.False);

			// The runs are the reader's: the tree names which derivation to draw, never the digits.
			Assert.That(elapsed.Property("format")!.Value.GetString(), Is.EqualTo(UiProgressFormats.Elapsed));
			Assert.That(duration.Property("format")!.Value.GetString(), Is.EqualTo(UiProgressFormats.Duration));
			Assert.That(elapsed.Property("text"), Is.Null);
			Assert.That(duration.Property("text"), Is.Null);
		});
	}

	[Test]
	public void A_paused_track_says_so_with_a_rate_of_zero_and_the_paused_badge()
	{
		var host = Render(Playing() with { IsPlaying = false, IsPaused = true, Position = HaltedAt(42_000) });

		Assert.Multiple(() =>
		{
			Assert.That(Node(host, "progress").Property("value")!.Value
					.GetProperty("$progress").GetProperty("rate").GetDouble(),
				Is.Zero,
				"a paused timeline must stop advancing on the reader's own clock");
			Assert.That(BadgeResourceId(host), Does.EndWith(".paused"));
		});
	}

	[Test]
	public void Pausing_dims_the_cover_and_drains_the_colour_out_of_the_timeline()
	{
		// Paused read as a dimmed, colourless widget before the migration, and that is what tells it
		// apart from playing at a glance - "watch whether the bar moves" is unreliable across a network
		// hiccup, which is why the retired component dimmed the artwork and greyed the fill.
		var playing = Render(Playing());
		var paused = Render(Playing() with { IsPlaying = false, IsPaused = true, Position = HaltedAt(42_000) });

		Assert.Multiple(() =>
		{
			// Brightness and saturation rather than opacity: opacity lets the ground show through, so
			// what a half-transparent cover looks like depends on the colour behind it - and the two
			// cover styles sit on different grounds.
			Assert.That(Node(playing, "cover").Property("brightness"), Is.Null, "a playing cover is untouched");
			Assert.That(Node(playing, "cover").Property("saturation"), Is.Null);
			Assert.That(Node(paused, "cover").Property("brightness")!.Value.GetDouble(), Is.EqualTo(0.6));
			Assert.That(Node(paused, "cover").Property("saturation")!.Value.GetDouble(), Is.EqualTo(0.55));

			Assert.That(Node(playing, "progress").Property("startColor")!.Value.GetString(),
				Is.EqualTo("#c9a6ff"));
			Assert.That(Node(paused, "progress").Property("startColor")!.Value.GetString(),
				Is.Not.EqualTo("#c9a6ff"),
				"a paused timeline drops the album accent");
			Assert.That(Node(paused, "progress").Property("startColor")!.Value.GetString(),
				Is.EqualTo(Node(paused, "progress").Property("endColor")!.Value.GetString()),
				"and goes flat rather than keeping a gradient");
		});
	}

	[Test]
	public void The_full_cover_style_dims_its_artwork_the_same_way_while_paused()
	{
		var playing = Render(Playing(), FullCover());
		var paused = Render(Playing() with { IsPlaying = false, IsPaused = true }, FullCover());

		Assert.Multiple(() =>
		{
			Assert.That(Node(playing, "artworkLayer").Property("brightness"), Is.Null);
			Assert.That(Node(paused, "artworkLayer").Property("brightness")!.Value.GetDouble(), Is.EqualTo(0.6));
			Assert.That(Node(paused, "artworkLayer").Property("saturation")!.Value.GetDouble(), Is.EqualTo(0.55));

			// The scrim substitute is not the paused dim and must not move with it: it is there to keep
			// the overlaid text legible whether or not playback is running.
			Assert.That(Node(paused, "artworkLayer").Property("opacity")!.Value.GetDouble(),
				Is.EqualTo(Node(playing, "artworkLayer").Property("opacity")!.Value.GetDouble()));
		});
	}

	[Test]
	public void A_playing_track_carries_the_playing_badge_and_a_stopped_one_carries_none()
	{
		var playing = Render(Playing());
		var stopped = Render(Playing() with { IsPlaying = false, IsPaused = false });

		Assert.Multiple(() =>
		{
			Assert.That(BadgeResourceId(playing), Does.EndWith(".playing"));
			Assert.That(FindNode(stopped, "badge"), Is.Null);
		});
	}

	[Test]
	public void The_cover_is_a_resource_handle_that_crossfades_rather_than_a_url()
	{
		var host = Render(Playing());
		var cover = Node(host, "cover");

		Assert.Multiple(() =>
		{
			Assert.That(cover.Type, Is.EqualTo(UiComponents.Image));
			Assert.That(cover.Property("source")!.Value.GetProperty("resourceId").GetString(),
				Is.EqualTo("app.macro-deck.music-player.artwork.test"));
			Assert.That(cover.Property("transition")!.Value.GetString(),
				Is.EqualTo(UiComponentImageTransitions.Crossfade));
		});
	}

	[Test]
	public void An_empty_player_shows_the_music_note_and_says_nothing_is_playing()
	{
		var host = Render(new MusicPlayerViewState { IsLoading = false, IsConnected = true });

		Assert.Multiple(() =>
		{
			Assert.That(Node(host, "coverPlaceholder").Property("source")!.Value.GetProperty("resourceId").GetString(),
				Does.EndWith(".music-note"));
			Assert.That(LocalizationKey(host, "title"),
				Is.EqualTo("Widgets.MusicPlayer.NothingPlaying"));
		});
	}

	[Test]
	public void A_loading_player_says_so_rather_than_claiming_a_player_it_has_not_reached_is_disconnected()
	{
		var host = Render(MusicPlayerViewState.Loading);

		Assert.Multiple(() =>
		{
			Assert.That(LocalizationKey(host, "title"), Is.EqualTo("Widgets.MusicPlayer.Loading"));
			// No reference at all, not a zero-position one: there is nothing yet to advance.
			Assert.That(Node(host, "progress").Property("value"), Is.Null);
		});
	}

	[Test]
	public void A_disconnected_player_asks_for_a_provider_and_an_unavailable_one_reports_its_status()
	{
		var disconnected = Render(new MusicPlayerViewState { IsLoading = false });
		var unavailable = Render(new MusicPlayerViewState
		{
			IsLoading = false,
			IsUnavailable = true,
			StatusMessage = "Rate limited",
		});

		Assert.Multiple(() =>
		{
			Assert.That(LocalizationKey(disconnected, "title"), Is.EqualTo("Widgets.MusicPlayer.NotConnected"));
			Assert.That(LocalizationKey(disconnected, "artist"), Is.EqualTo("Widgets.MusicPlayer.SetUpProvider"));

			Assert.That(LocalizationKey(unavailable, "title"), Is.EqualTo("Widgets.MusicPlayer.Unavailable"));
			Assert.That(Node(unavailable, "artist").Property("text")!.Value.GetString(), Is.EqualTo("Rate limited"));
			// A player that cannot be reached has no live state to report, so the warning wins the corner.
			Assert.That(BadgeResourceId(unavailable), Does.EndWith(".warning"));
		});
	}

	[Test]
	public void A_stale_instance_selection_wins_the_corner_over_every_other_badge()
	{
		var host = Render(Playing() with { InstanceMissing = true });

		Assert.That(BadgeResourceId(host),
			Does.EndWith(".warning"),
			"a stale selection is the more specific problem, so it must not be hidden behind a playback badge");
	}

	[Test]
	public void The_placeholder_disc_turns_only_while_a_loaded_track_is_playing()
	{
		var playing = Render(Playing() with { Artwork = null });
		var paused = Render(Playing() with { Artwork = null, IsPlaying = false, IsPaused = true });

		Assert.Multiple(() =>
		{
			Assert.That(PlaceholderResourceId(playing), Does.EndWith(".disc-spinning"));
			Assert.That(PlaceholderResourceId(paused), Does.EndWith(".disc"));
		});
	}

	[Test]
	public void The_full_cover_style_draws_the_artwork_behind_the_text_without_offering_a_press()
	{
		var host = Render(Playing(), FullCover());
		var layer = Node(host, "artworkLayer");

		Assert.Multiple(() =>
		{
			Assert.That(layer.Type, Is.EqualTo(UiComponents.Button));
			Assert.That(layer.Property("events"), Is.Null, "the artwork layer exists to carry a picture");
			Assert.That(layer.Property("fit")!.Value.GetString(), Is.EqualTo(UiComponentImageFits.Cover));
			Assert.That(layer.Property("transition")!.Value.GetString(),
				Is.EqualTo(UiComponentImageTransitions.Crossfade));
			// Never absent on this branch: absence on a button means the reader's own accent colour.
			Assert.That(layer.Property("background")!.Value.GetString(), Is.EqualTo("#241832"));
		});
	}

	[Test]
	public void The_full_cover_artwork_follows_the_corner_of_the_tile_it_fills()
	{
		// Issue #866: the artwork layer is drawn to the tile's edges, so it is the tile's face. Without a
		// stated corner a reader rounds it by a share of its own height, which is an arc cut across a tile
		// drawn with any other radius - and the view cannot compute one for itself, since it never learns
		// the height it is drawn at.
		var host = Render(Playing(), FullCover());

		Assert.That(Node(host, "artworkLayer").Property("corner")?.GetString(),
			Is.EqualTo(UiComponentButtonCorners.Tile));
	}

	[Test]
	public void The_full_cover_style_falls_back_to_a_plain_layer_when_there_is_no_artwork()
	{
		var host = Render(Playing() with { Artwork = null, Background = null }, FullCover());

		Assert.Multiple(() =>
		{
			Assert.That(FindNode(host, "artworkLayer"), Is.Null);
			Assert.That(Node(host, "placeholderLayer").Type, Is.EqualTo(UiComponents.Stack));
			// The tile's own surface shows through, so a themeless widget still follows the reader's theme.
			Assert.That(Node(host, "fullCover").Property("background"), Is.Null);
		});
	}

	[Test]
	public void Artwork_colours_reach_the_tree_as_literals_so_no_renderer_has_to_average_a_cover()
	{
		var host = Render(Playing());

		Assert.Multiple(() =>
		{
			Assert.That(Node(host, "smallCover").Property("background")!.Value.GetString(),
				Is.EqualTo("#241832"));
			Assert.That(Node(host, "progress").Property("startColor")!.Value.GetString(), Is.EqualTo("#c9a6ff"));
			Assert.That(Node(host, "progress").Property("endColor")!.Value.GetString(), Is.EqualTo("#c9a6ff"));
		});
	}

	[Test]
	public void A_widget_with_no_artwork_colour_leaves_the_timeline_on_the_readers_accent()
	{
		var host = Render(Playing() with { Accent = null, Background = null });

		Assert.Multiple(() =>
		{
			Assert.That(Node(host, "progress").Property("startColor"), Is.Null);
			Assert.That(Node(host, "progress").Property("endColor"), Is.Null);
		});
	}

	[Test]
	public void Turning_a_row_off_removes_it_from_the_tree_rather_than_blanking_it()
	{
		var host = Render(Playing(),
			new MusicPlayerWidgetData
			{
				ShowHeader = false,
				ShowTitle = false,
				ShowArtist = false,
				ShowAlbum = false,
				ShowTimeline = false,
			});

		Assert.Multiple(() =>
		{
			Assert.That(FindNode(host, "providerName"), Is.Null);
			Assert.That(FindNode(host, "title"), Is.Null);
			Assert.That(FindNode(host, "artist"), Is.Null);
			Assert.That(FindNode(host, "album"), Is.Null);
			Assert.That(FindNode(host, "progress"), Is.Null);
			// The badge outlives the header it usually rides in - it is the answer to "is this playing".
			Assert.That(FindNode(host, "badge"), Is.Not.Null);
		});
	}

	[Test]
	public void A_track_change_repaints_through_property_patches_rather_than_a_structural_rebuild()
	{
		var state = new UiState<MusicPlayerViewState>(Playing());
		var config = new UiState<MusicPlayerWidgetData>(new MusicPlayerWidgetData());
		var host = UiTestHost.Render(MusicPlayerWidgetView.Build(state, config, Icons()), WidgetSurface());
		host.ClearPatches();

		state.Value = Playing() with
		{
			TrackName = "Nannou",
			Position = UiProgressReference.Advancing(0, _anchor, 189_000),
		};

		Assert.Multiple(() =>
		{
			Assert.That(host.Patches, Is.Not.Empty);
			Assert.That(host.Patches.SelectMany(patch => patch.Operations).Select(operation => operation.Op),
				Has.All.EqualTo(UiPatchOperations.SetProperties),
				"a track change must not reconcile the tree - see the issue's efficiency requirement");
			Assert.That(Node(host, "title").Property("text")!.Value.GetString(), Is.EqualTo("Nannou"));
		});
	}

	[Test]
	public void Writing_the_same_state_again_emits_nothing_at_all()
	{
		var playing = Playing();
		var state = new UiState<MusicPlayerViewState>(playing);
		var config = new UiState<MusicPlayerWidgetData>(new MusicPlayerWidgetData());
		var host = UiTestHost.Render(MusicPlayerWidgetView.Build(state, config, Icons()), WidgetSurface());
		host.ClearPatches();

		// The session re-resolves on every state change of every instance, so an unrelated one must cost
		// nothing on the wire.
		state.Value = playing with { };

		Assert.That(host.Patches, Is.Empty);
	}

	[Test]
	public void Saving_a_new_cover_style_switches_an_open_session_rather_than_waiting_for_a_reopen()
	{
		// Reported against the first build: a widget saved as "full cover" came back as the small one on
		// the next start and only switched after the editor was opened again, because the layout was
		// chosen once when the session was built and a shared widget session outlives an editor save.
		var state = new UiState<MusicPlayerViewState>(Playing());
		var config = new UiState<MusicPlayerWidgetData>(new MusicPlayerWidgetData());
		var host = UiTestHost.Render(MusicPlayerWidgetView.Build(state, config, Icons()), WidgetSurface());

		Assert.That(host.FindById("musicPlayer.smallCover"), Is.Not.Null);

		config.Value = FullCover();

		Assert.Multiple(() =>
		{
			Assert.That(host.FindById("musicPlayer.fullCover"), Is.Not.Null);
			Assert.That(host.FindById("musicPlayer.smallCover"), Is.Null);
		});
	}

	[Test]
	public void Hiding_a_row_from_the_editor_reaches_an_open_session_too()
	{
		var state = new UiState<MusicPlayerViewState>(Playing());
		var config = new UiState<MusicPlayerWidgetData>(new MusicPlayerWidgetData());
		var host = UiTestHost.Render(MusicPlayerWidgetView.Build(state, config, Icons()), WidgetSurface());

		Assert.That(FindNode(host, "progress"), Is.Not.Null);

		config.Value = new MusicPlayerWidgetData { ShowTimeline = false };

		Assert.That(FindNode(host, "progress"), Is.Null);
	}

	[Test]
	public void The_timeline_track_has_a_box_to_sit_in_rather_than_collapsing_to_nothing()
	{
		// A leaf in a vertical stack has no intrinsic height, so a bar given only its thickness resolves
		// to a zero-height box and paints its track outside the layout - which is what pushed the times
		// row off the bottom edge of the tile in the first build.
		var host = Render(Playing());
		var bar = Node(host, "progress");

		Assert.Multiple(() =>
		{
			Assert.That(bar.Property("mainSize"), Is.Not.Null, "a progress bar must claim height of its own");
			Assert.That(bar.Property("mainSize")!.Value.GetProperty("basis").GetDouble(),
				Is.EqualTo(bar.Property("thickness")!.Value.GetProperty("basis").GetDouble()),
				"the box is the track, so the two have to agree");
		});
	}

	[Test]
	public void Every_size_is_calibrated_against_a_two_cell_widget_rather_than_a_one_cell_one()
	{
		// The retired component laid out at 120px per cell, so its fixed pixel values were fractions of
		// 240 on the two-cell widget it was actually used at. Converting against 120 instead doubled
		// every one of them - a 16px provider icon came out at 44px on a 2x2 tile.
		var host = Render(Playing());

		Assert.Multiple(() =>
		{
			Assert.That(Basis(Node(host, "providerIcon"), "size"), Is.EqualTo(16.0 / 240).Within(0.002));
			Assert.That(Basis(Node(host, "title"), "size"), Is.EqualTo(13.0 / 240).Within(0.002));
			Assert.That(Basis(Node(host, "artist"), "size"), Is.EqualTo(11.0 / 240).Within(0.002));
			Assert.That(Basis(Node(host, "elapsed"), "size"), Is.EqualTo(9.0 / 240).Within(0.002));
			Assert.That(Basis(Node(host, "progress"), "thickness"), Is.EqualTo(6.0 / 240).Within(0.002));
		});
	}

	/// <summary>
	/// The one length here that is no longer calibrated against the basis at all. The edge clearance is
	/// shared by every widget and anchored to the reference cell instead, because the corner it clears is
	/// drawn the same size whatever the widget spans (ADR 0064) - a two-cell tile carries the same corner
	/// as a one-cell one, so the inset that clears it is the same length rather than the same fraction.
	/// </summary>
	[Test]
	public void TheEdgeClearanceIsTheOneEveryWidgetShares()
	{
		var host = Render(Playing());
		var padding = Node(host, "smallCover").Property("padding")!.Value;

		Assert.Multiple(() =>
		{
			Assert.That(padding.GetProperty("basis").GetDouble(),
				Is.EqualTo(WidgetSafeArea.LengthFor(WidgetSafeArea.DefaultCornerRadius).Basis).Within(1e-9));
			Assert.That(padding.GetProperty("maxOfCell").GetDouble(),
				Is.EqualTo(WidgetSafeArea.LengthFor(WidgetSafeArea.DefaultCornerRadius).MaxOfCell!.Value)
					.Within(1e-9),
				"anchored to the cell, or it would grow with the widget and over-pad a large tile");
		});
	}

	private static double Basis(UiTestNode node, string key)
		=> node.Property(key)!.Value.GetProperty("basis").GetDouble();

	private static MusicPlayerWidgetData FullCover()
		=> new() { CoverStyle = MusicPlayerWidgetData.FullCoverStyle };

	private static MusicPlayerViewState Playing()
		=> new()
		{
			IsLoading = false,
			IsConnected = true,
			IsPlaying = true,
			Label = LocalizedText.FromLiteral("Spotify"),
			ProviderIcon = new UiResource { ResourceId = "app.macro-deck.music-player.provider.test" },
			TrackName = "Windowlicker",
			ArtistName = "Aphex Twin",
			AlbumName = "Windowlicker",
			Artwork = new UiResource { ResourceId = "app.macro-deck.music-player.artwork.test" },
			Accent = "#c9a6ff",
			Background = "#241832",
			Position = UiProgressReference.Advancing(42_000, _anchor, 215_000),
		};

	private static UiProgressReference HaltedAt(long positionMs)
		=> UiProgressReference.Halted(positionMs, _anchor, 215_000);

	private static string BadgeResourceId(UiTestHost host)
		=> Node(host, "badge").Property("source")!.Value.GetProperty("resourceId").GetString()!;

	/// <summary>
	/// A node by the key its element declared, wherever the tree put it. Node ids are the whole path
	/// down from the root, so addressing by key keeps an assertion about <i>what</i> a node carries from
	/// also asserting where it sits - which is what would otherwise make every one of these tests fail on
	/// a purely structural change.
	/// </summary>
	private static UiTestNode Node(UiTestHost host, string key) => FindNode(host, key)!;

	private static UiTestNode? FindNode(UiTestHost host, string key)
		=> Walk(host.Root).SingleOrDefault(node
			=> string.Equals(node.Id, key, StringComparison.Ordinal) ||
			node.Id.EndsWith($".{key}", StringComparison.Ordinal));

	private static string PlaceholderResourceId(UiTestHost host)
		=> Node(host, "coverPlaceholder").Property("source")!.Value.GetProperty("resourceId").GetString()!;

	private static string? LocalizationKey(UiTestHost host, string nodeId)
		=> Node(host, nodeId).Property("text")!.Value.GetProperty("$localized").GetProperty("key").GetString();

	private static MusicPlayerIconResources Icons()
		=> MusicPlayerWidgetIcons.EnsureRegistered(new UiResourceStore());

	private static UiTestHost Render(MusicPlayerViewState value, MusicPlayerWidgetData? config = null)
	{
		var state = new UiState<MusicPlayerViewState>(value);
		var configState = new UiState<MusicPlayerWidgetData>(config ?? new MusicPlayerWidgetData());

		return UiTestHost.Render(MusicPlayerWidgetView.Build(state, configState, Icons()), WidgetSurface());
	}

	private static UiSurface WidgetSurface()
		=> new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared };

	private static IEnumerable<UiTestNode> Walk(UiTestNode node)
	{
		yield return node;

		foreach (var child in node.Children)
		{
			foreach (var descendant in Walk(child))
			{
				yield return descendant;
			}
		}

		if (node.Fallback is not null)
		{
			foreach (var descendant in Walk(node.Fallback))
			{
				yield return descendant;
			}
		}
	}
}
