using MacroDeck.Localization;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.References;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Components;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Widgets.MusicPlayer;

/// <summary>
/// The Music Player's tree. Every value that moves is a lazy closure over the view state, so a
/// track change, a pause or a re-anchored position is a <c>set-properties</c> patch on the handful of
/// nodes that carry it rather than a structural reconcile - which is what the profile's binding model is
/// for and what the timeline in particular needs, since it is the part that changes most often.
///
/// <para>
/// <b>The tree declares no events.</b> The press that runs the widget's flows is the deck tile's own, the
/// way it already was for this widget and still is for Weather, Clock and History Graph:
/// <c>UiTreeWidgetComponent</c> keeps the gesture for a tree in which no node claims it. The one
/// <c>ui.button</c> below - the full cover style's artwork layer - deliberately declares none for the
/// same reason: it is there to carry artwork behind children, not to be pressed.
/// </para>
///
/// <para>
/// <b>Sizes are fractions of the widget basis</b>, converted from the retired component's
/// reference-pixel values <b>over a two-cell widget</b> - a 240px reference box, since
/// <c>WIDGET_REFERENCE_CELL_SIZE</c> is 120 per cell. That is the calibration target because every one
/// of that component's <c>clamp()</c> font sizes is already saturated at its maximum by two cells, so
/// two cells and up is the shape it converged to: a 10px padding is <c>0.042</c>, a 16px provider icon
/// is <c>0.067</c>, a 6px timeline track is <c>0.025</c>. Converting against one cell instead would
/// double every one of them at the size this widget is actually used at.
/// </para>
///
/// <para>
/// The component's <c>compact</c>/<c>mini</c> breakpoints have no counterpart here on purpose (#749):
/// widget content is scaled uniformly from its reference box, so one set of proportions is correct at
/// every deck size, and a one-cell Music Player is the same picture drawn smaller.
/// </para>
/// </summary>
internal static class MusicPlayerWidgetView
{
	private const double _gap = 0.027;
	private const double _rowGap = 0.019;
	private const double _infoGap = 0.00625;
	private const double _timelineGap = 0.0104;
	private const double _bottomGap = 0.021;

	private const double _providerIconSize = 0.067;
	private const double _badgeSize = 0.083;

	// The track's own thickness, and the box it sits centred in. Both, because a leaf in a vertical
	// stack has no intrinsic height: a bar given only its thickness resolves to a zero-height box and
	// paints its track outside the layout entirely.
	private const double _trackThickness = 0.025;

	// The full cover style dims its artwork instead of laying a gradient scrim over it: the profile has
	// no gradient, and a uniform dim over the artwork's own darkened average keeps the overlaid text
	// legible for the same reason the scrim did. Deliberately light - the scrim it replaces left the top
	// of the cover untouched, so a heavy uniform dim reads as a duller widget rather than a legible one.
	private const double _fullArtworkOpacity = 0.88;

	// Paused reads as a dimmed, colourless widget, which is how the retired component told it apart from
	// playing at a glance: the cover loses light and colour, and the timeline drops the album accent.
	// Both survive the network hiccups that make "watch whether the bar moves" an unreliable way to tell
	// the two apart. The two numbers are that component's own filter, which the profile gained a spelling
	// for so this could stay the same picture rather than an approximation of it.
	private const double _pausedBrightness = 0.6;
	private const double _pausedSaturation = 0.55;

	// The timeline's colour while paused. A literal grey rather than a text role, because a bar takes
	// only literal colours - and "no colour" is the point here, so it reads the same in either theme.
	private const string _pausedTimelineColor = "#8c8c92";

	/// <summary>
	/// The whole tree, with the cover style chosen <b>inside</b> it rather than by which method built it.
	/// The two layouts are different enough to be two subtrees, but picking one at build time would freeze
	/// it for the session's whole life - and a widget session is shared and long-lived, so a cover style
	/// saved in the editor would not reach the deck until something else happened to reopen the session.
	/// </summary>
	public static UiElement Build(
		UiState<MusicPlayerViewState> state,
		UiState<MusicPlayerWidgetData> config,
		MusicPlayerIconResources icons,
		int cornerRadius = WidgetSafeArea.DefaultCornerRadius)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(icons);

		// The artwork stays edge to edge - it is the tile's background, not its content - so the clearance
		// applies to what is laid over it. See ADR 0064.
		var safeArea = WidgetSafeArea.For(cornerRadius);

		return new UiStack
		{
			Key = "musicPlayer",
			Direction = UiComponentDirections.Vertical,
			Children =
			[
				new UiWhen
				{
					Key = "smallCoverGate",
					Condition = () => !config.Value.IsFullCover,
					Content = () => BuildSmallCover(state, config, icons, safeArea),
				},
				new UiWhen
				{
					Key = "fullCoverGate",
					Condition = () => config.Value.IsFullCover,
					Content = () => BuildFullCover(state, config, icons, safeArea),
				},
			],
		};
	}

	private static UiStack BuildSmallCover(
		UiState<MusicPlayerViewState> state,
		UiState<MusicPlayerWidgetData> config,
		MusicPlayerIconResources icons,
		UiSize safeArea)
		=> new()
		{
			Key = "smallCover",
			Fill = true,
			Direction = UiComponentDirections.Vertical,
			Padding = safeArea,
			Gap = _gap,
			Background = BackgroundColor(state),
			Children =
			[
				new UiWhen
				{
					Key = "headerGate",
					Condition = () => HasHeaderRow(state, config),
					Content = () => BuildHeaderRow(state, config, icons),
				},
				new UiStack
				{
					Key = "body",
					Direction = UiComponentDirections.Vertical,
					Justify = UiComponentJustify.Center,
					Align = UiComponentAlignments.Center,
					Gap = _gap,
					Fill = true,
					Children =
					[
						BuildCoverRow(state, icons),
						new UiWhen
						{
							Key = "infoGate",
							Condition = () => HasInfo(state, config),
							Content = () => BuildInfo(state, config, UiComponentAlignments.Center),
						},
					],
				},
				new UiWhen
				{
					Key = "timelineGate",
					Condition = () => config.Value.ShowTimeline,
					Content = () => BuildTimeline(state),
				},
			],
		};

	private static UiStack BuildFullCover(
		UiState<MusicPlayerViewState> state,
		UiState<MusicPlayerWidgetData> config,
		MusicPlayerIconResources icons,
		UiSize safeArea)
	{
		// Built once and shared between the two branches below, the way ActionButtonWidgetView shares its
		// label with its own fallback: only one of them is ever materialized, and authoring the overlay
		// twice would be two things to keep in step.
		var header = new UiWhen
		{
			Key = "headerGate",
			Condition = () => HasHeaderRow(state, config),
			Content = () => BuildHeaderRow(state, config, icons),
		};

		var bottom = new UiStack
		{
			Key = "bottom",
			Direction = UiComponentDirections.Vertical,
			Gap = _bottomGap,
			Children =
			[
				new UiWhen
				{
					Key = "infoGate",
					Condition = () => HasInfo(state, config),
					Content = () => BuildInfo(state, config, UiComponentAlignments.Start),
				},
				new UiWhen
				{
					Key = "timelineGate",
					Condition = () => config.Value.ShowTimeline,
					Content = () => BuildTimeline(state),
				},
			],
		};

		return new UiStack
		{
			Key = "fullCover",
			Fill = true,
			Direction = UiComponentDirections.Vertical,
			Background = BackgroundColor(state),
			Children =
			[
				new UiWhen
				{
					Key = "artworkGate",
					Condition = () => state.Value.Artwork is not null,
					Content = () => new UiButton
					{
						Key = "artworkLayer",
						Fill = true,
						// This layer is the tile's face, so it takes the tile's corner. Left to the profile's own
						// rule it would round itself by a share of its own height instead - an arc cut across a
						// tile drawn with any other radius, which is issue #866.
						Corner = UiComponentButtonCorners.Tile,
						Direction = UiComponentDirections.Vertical,
						Justify = UiComponentJustify.SpaceBetween,
						Padding = safeArea,
						// Never absent on this branch: absence on a button means the reader's accent colour,
						// and black behind an artwork drawn to cover is invisible anyway.
						Background = UiValue.From(() => state.Value.Background ?? "#000000"),
						Source = UiValue.Optional(() => state.Value.Artwork is { } artwork
							? UiValue.Of(artwork)
							: UiValue.None<UiResource>()),
						Fit = UiComponentImageFits.Cover,
						Opacity = _fullArtworkOpacity,
						Brightness = PausedBrightness(state),
						Saturation = PausedSaturation(state),
						Transition = UiComponentImageTransitions.Crossfade,
						Children = [header, bottom],
					},
				},
				new UiWhen
				{
					Key = "placeholderGate",
					Condition = () => state.Value.Artwork is null,
					Content = () => new UiStack
					{
						Key = "placeholderLayer",
						Fill = true,
						Direction = UiComponentDirections.Vertical,
						Justify = UiComponentJustify.SpaceBetween,
						Padding = safeArea,
						Children =
						[
							header,
							new UiStack
							{
								Key = "placeholderRow",
								Fill = true,
								Direction = UiComponentDirections.Horizontal,
								Justify = UiComponentJustify.Center,
								Align = UiComponentAlignments.Center,
								Children = [BuildPlaceholderIcon(state, icons, 0.32)],
							},
							bottom,
						],
					},
				},
			],
		};
	}

	private static UiStack BuildHeaderRow(
		UiState<MusicPlayerViewState> state,
		UiState<MusicPlayerWidgetData> config,
		MusicPlayerIconResources icons)
		=> new()
		{
			Key = "header",
			Direction = UiComponentDirections.Horizontal,
			Align = UiComponentAlignments.Center,
			Justify = UiComponentJustify.SpaceBetween,
			Gap = _rowGap,
			Children =
			[
				new UiStack
				{
					Key = "provider",
					Fill = true,
					Direction = UiComponentDirections.Horizontal,
					Align = UiComponentAlignments.Center,
					Gap = _rowGap,
					Children =
					[
						new UiWhen
						{
							Key = "providerIconGate",
							Condition = () => config.Value.ShowHeader && state.Value.ProviderIcon is not null,
							Content = () => new UiImage
							{
								Key = "providerIcon",
								Size = _providerIconSize,
								Source = UiValue.Optional(() => state.Value.ProviderIcon is { } icon
									? UiValue.Of(icon)
									: UiValue.None<UiResource>()),
							},
						},
						new UiWhen
						{
							Key = "providerNameGate",
							Condition = () => config.Value.ShowHeader && HasLabel(state.Value.Label),
							Content = () => new UiTextRun
							{
								Key = "providerName",
								Size = 0.044,
								MinSize = 0.034,
								Weight = UiComponentTextWeights.SemiBold,
								Role = UiComponentTextRoles.Secondary,
								Text = UiText.Optional(() => state.Value.Label),
							},
						},
					],
				},
				new UiWhen
				{
					Key = "badgeGate",
					Condition = () => BadgeIcon(state, icons) is not null,
					Content = () => new UiImage
					{
						Key = "badge",
						Size = _badgeSize,
						Source = UiValue.Optional(() => BadgeIcon(state, icons) is { } badge
							? UiValue.Of(badge)
							: UiValue.None<UiResource>()),
					},
				},
			],
		};

	private static UiStack BuildCoverRow(UiState<MusicPlayerViewState> state, MusicPlayerIconResources icons)
		=> new()
		{
			Key = "coverRow",
			Fill = true,
			Direction = UiComponentDirections.Horizontal,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Children =
			[
				new UiWhen
				{
					Key = "coverGate",
					Condition = () => state.Value.Artwork is not null,
					Content = () => new UiImage
					{
						Key = "cover",
						// The cover takes whatever height the rows around it left, capped at the widget's
						// own basis - which is exactly what the retired component's `min(height - reserved,
						// width - 24)` computed, expressed as a clamp the reader resolves instead of a
						// pixel the host would have to recompute on every resize.
						Size = UiSize.FromBasis(0.9, 1.0),
						Transition = UiComponentImageTransitions.Crossfade,
						Brightness = PausedBrightness(state),
						Saturation = PausedSaturation(state),
						Source = UiValue.Optional(() => state.Value.Artwork is { } artwork
							? UiValue.Of(artwork)
							: UiValue.None<UiResource>()),
					},
				},
				new UiWhen
				{
					Key = "coverPlaceholderGate",
					Condition = () => state.Value.Artwork is null,
					Content = () => BuildPlaceholderIcon(state, icons, 0.45),
				},
			],
		};

	/// <summary>The mark shown where there is no cover: a music note while nothing is loaded, a disc once
	/// something is - turning while it plays, still while it does not.</summary>
	private static UiImage BuildPlaceholderIcon(
		UiState<MusicPlayerViewState> state,
		MusicPlayerIconResources icons,
		double size)
		=> new()
		{
			Key = "coverPlaceholder",
			Size = UiSize.FromBasis(size, size / 0.86),
			Source = UiValue.From(() =>
			{
				var value = state.Value;

				if (!value.HasTrack)
				{
					return icons.MusicNote;
				}

				return value is { IsPlaying: true, IsConnected: true } ? icons.DiscSpinning : icons.Disc;
			}),
		};

	private static UiStack BuildInfo(
		UiState<MusicPlayerViewState> state,
		UiState<MusicPlayerWidgetData> config,
		string align)
		=> new()
		{
			Key = "info",
			Direction = UiComponentDirections.Vertical,
			Align = align,
			Gap = _infoGap,
			Children =
			[
				new UiWhen
				{
					Key = "titleGate",
					Condition = () => config.Value.ShowTitle,
					Content = () => new UiTextRun
					{
						Key = "title",
						Size = 0.054,
						MinSize = 0.042,
						Weight = UiComponentTextWeights.SemiBold,
						Role = UiComponentTextRoles.Primary,
						Align = align,
						Text = UiText.Optional(() => PlaceholderTitle(state.Value)),
					},
				},
				new UiWhen
				{
					Key = "artistGate",
					Condition = () => config.Value.ShowArtist,
					Content = () => new UiTextRun
					{
						Key = "artist",
						Size = 0.046,
						MinSize = 0.036,
						Role = UiComponentTextRoles.Secondary,
						Align = align,
						Text = UiText.Optional(() => PlaceholderSubtitle(state.Value)),
					},
				},
				new UiWhen
				{
					Key = "albumGate",
					Condition = () => config.Value.ShowAlbum &&
						!config.Value.IsFullCover &&
						!string.IsNullOrEmpty(state.Value.AlbumName),
					Content = () => new UiTextRun
					{
						Key = "album",
						Size = 0.044,
						MinSize = 0.034,
						Role = UiComponentTextRoles.Muted,
						Align = align,
						Text = UiText.From(() => state.Value.AlbumName),
					},
				},
			],
		};

	private static UiStack BuildTimeline(UiState<MusicPlayerViewState> state)
		=> new()
		{
			Key = "timeline",
			Direction = UiComponentDirections.Vertical,
			Gap = _timelineGap,
			Children =
			[
				new UiProgressBar
				{
					Key = "progress",
					MainSize = _trackThickness,
					Thickness = _trackThickness,
					// One colour at both ends: the retired component painted a flat fill, and a gradient
					// between two shades of one derived colour would be a change, not a migration. Absent
					// leaves the reader on its own accent, which is what a widget with no artwork showed.
					StartColor = AccentColor(state),
					EndColor = AccentColor(state),
					Value = UiValue.Optional(() => state.Value.Position is { } position
						? UiValue.Of(position)
						: UiValue.None<UiProgressReference>()),
				},
				new UiStack
				{
					Key = "times",
					Direction = UiComponentDirections.Horizontal,
					Justify = UiComponentJustify.SpaceBetween,
					Children =
					[
						BuildTime(state, "elapsed", UiProgressFormats.Elapsed, UiComponentAlignments.Start),
						BuildTime(state, "duration", UiProgressFormats.Duration, UiComponentAlignments.End),
					],
				},
			],
		};

	private static UiProgressText BuildTime(
		UiState<MusicPlayerViewState> state,
		string key,
		string format,
		string align)
		=> new()
		{
			Key = key,
			Format = format,
			Size = 0.0375,
			MinSize = 0.029,
			Role = UiComponentTextRoles.Muted,
			Align = align,
			Value = UiValue.Optional(() => state.Value.Position is { } position
				? UiValue.Of(position)
				: UiValue.None<UiProgressReference>()),
		};

	/// <summary>The light a paused cover loses. Absent while playing, so an untouched cover carries
	/// neither key.</summary>
	private static UiValue<double> PausedBrightness(UiState<MusicPlayerViewState> state)
		=> UiValue.Optional(() => state.Value.IsPaused
			? UiValue.Of(_pausedBrightness)
			: UiValue.None<double>());

	/// <summary>The colour a paused cover loses. See <see cref="PausedBrightness" />.</summary>
	private static UiValue<double> PausedSaturation(UiState<MusicPlayerViewState> state)
		=> UiValue.Optional(() => state.Value.IsPaused
			? UiValue.Of(_pausedSaturation)
			: UiValue.None<double>());

	private static UiValue<string> BackgroundColor(UiState<MusicPlayerViewState> state)
		=> UiValue.Optional(() => state.Value.Background is { } background
			? UiValue.Of(background)
			: UiValue.None<string>());

	/// <summary>The timeline's colour: the artwork's accent while playing, a flat grey while paused, and
	/// absent - the reader's own accent - when there is no artwork to borrow a colour from.</summary>
	private static UiValue<string> AccentColor(UiState<MusicPlayerViewState> state)
		=> UiValue.Optional(() =>
		{
			if (state.Value.IsPaused)
			{
				return UiValue.Of(_pausedTimelineColor);
			}

			return state.Value.Accent is { } accent ? UiValue.Of(accent) : UiValue.None<string>();
		});

	private static bool HasHeaderRow(UiState<MusicPlayerViewState> state,
		UiState<MusicPlayerWidgetData> config)
	{
		var value = state.Value;

		if (value.ShowWarningBadge || value.ShowPlaybackBadge)
		{
			return true;
		}

		return config.Value.ShowHeader && (value.ProviderIcon is not null || HasLabel(value.Label));
	}

	private static bool HasLabel(LocalizedText label)
		=> label.Localized is not null || label.Literal is { Length: > 0 };

	private static bool HasInfo(UiState<MusicPlayerViewState> state,
		UiState<MusicPlayerWidgetData> config)
		=> config.Value.ShowTitle ||
			config.Value.ShowArtist ||
			(config.Value.ShowAlbum && !config.Value.IsFullCover && !string.IsNullOrEmpty(state.Value.AlbumName));

	/// <summary>The badge in the header's trailing corner. A stale selection is the more specific problem,
	/// so it wins over the outage hint, and both win over the playback badge.</summary>
	private static UiResource? BadgeIcon(UiState<MusicPlayerViewState> state, MusicPlayerIconResources icons)
	{
		var value = state.Value;

		if (value.ShowWarningBadge)
		{
			return icons.Warning;
		}

		if (!value.ShowPlaybackBadge)
		{
			return null;
		}

		return value.IsPaused ? icons.Paused : icons.Playing;
	}

	/// <summary>The headline: the track, or what is standing in for one.</summary>
	private static UiText PlaceholderTitle(MusicPlayerViewState state)
	{
		if (state.TrackName is { Length: > 0 } track)
		{
			return UiText.Of(track);
		}

		if (state.IsLoading)
		{
			return UiText.Of(AppStrings.Widgets.MusicPlayer.Loading());
		}

		if (state.IsConnected)
		{
			return UiText.Of(AppStrings.Widgets.MusicPlayer.NothingPlaying());
		}

		return UiText.Of(state.IsUnavailable
			? AppStrings.Widgets.MusicPlayer.Unavailable()
			: AppStrings.Widgets.MusicPlayer.NotConnected());
	}

	/// <summary>The second line: the artist, or the reason there is none.</summary>
	private static UiText PlaceholderSubtitle(MusicPlayerViewState state)
	{
		if (state.ArtistName is { Length: > 0 } artist)
		{
			return UiText.Of(artist);
		}

		if (state.IsLoading || state.IsConnected)
		{
			return UiText.Of("-");
		}

		if (!state.IsUnavailable)
		{
			return UiText.Of(AppStrings.Widgets.MusicPlayer.SetUpProvider());
		}

		return state.StatusMessage is { Length: > 0 } status
			? UiText.Of(status)
			: UiText.Of(AppStrings.Widgets.MusicPlayer.Reconnecting());
	}
}
