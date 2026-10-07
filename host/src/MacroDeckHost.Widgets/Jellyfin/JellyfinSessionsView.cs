using MacroDeck.Localization;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.JellyfinSessions;

namespace MacroDeckHost.Widgets.Jellyfin;

internal static class JellyfinSessionsView
{
	public const string Root = "jellyfinSessions";

	public const int TallRows = 3;
	public const int ListRows = 4;

	// Jellyfin's own brand gradient, so the progress reads the same on any widget background.
	private const string ProgressStart = "#aa5cc3";
	private const string ProgressEnd = "#00a4dc";
	private const string ArtworkPlaceholder = "#2c2c2e";
	private const double ProgressThickness = 0.018;

	private static readonly UiLength _glyph = UiLength.Capped(0.12, 15);
	private static readonly UiLength _badgeText = UiLength.Capped(0.08, 10);
	private static readonly UiLength _headingText = UiLength.Capped(0.085, 11.5);
	private static readonly UiLength _heroTitle = UiLength.Capped(0.13, 17);
	private static readonly UiLength _title = UiLength.Capped(0.1, 13.5);
	private static readonly UiLength _subtitle = UiLength.Capped(0.085, 11.5);
	private static readonly UiLength _detail = UiLength.Capped(0.075, 10);
	private static readonly UiLength _smallArtwork = UiLength.Capped(0.46, 56);
	private static readonly UiLength _thumb = UiLength.Capped(0.2, 34);
	private static readonly UiLength _gap = UiLength.Capped(0.04, 5);
	private static readonly UiLength _rowGap = UiLength.Capped(0.06, 8);

	public static UiElement Build(
		UiState<JellyfinSessionsState> state,
		int cornerRadius = WidgetSafeArea.DefaultCornerRadius,
		string? backgroundColor = null)
	{
		ArgumentNullException.ThrowIfNull(state);

		return new UiStack
		{
			Key = Root,
			Direction = UiComponentDirections.Vertical,
			Padding = WidgetSafeArea.For(cornerRadius),
			Background = backgroundColor is { } background ? UiValue.Of(background) : UiValue.None<string>(),
			Children =
			[
				new UiWhen
				{
					Key = "noServerGate",
					Condition = () => state.Value.Status == JellyfinSessionsStatus.NoServer,
					Content = () => Notice(state, "noServer", Strings.NoServer()),
				},
				new UiWhen
				{
					Key = "notConnectedGate",
					Condition = () => state.Value.Status == JellyfinSessionsStatus.NotConnected,
					Content = () => Notice(state, "notConnected", Strings.NotConnected()),
				},
				new UiWhen
				{
					Key = "idleGate",
					Condition = () => state.Value is { Status: JellyfinSessionsStatus.Ready, Count: 0 },
					Content = () => Notice(state, "idle", Strings.NothingPlaying()),
				},
				new UiWhen
				{
					Key = "playingGate",
					Condition = () => state.Value is { Status: JellyfinSessionsStatus.Ready, Count: > 0 },
					Content = () => Layout(state),
				},
			],
		};
	}

	private static UiResponsive Layout(UiState<JellyfinSessionsState> state)
		=> new()
		{
			Key = "layout",
			Fill = true,
			Default = Small(state),
			Variants =
			[
				new UiResponsiveVariant { MinWidth = 1.5, MinHeight = 1.5, Content = Large(state) },
				new UiResponsiveVariant { MinHeight = 1.5, Content = List(state, TallRows, withArtwork: false) },
				new UiResponsiveVariant { MinWidth = 1.5, Content = Medium(state) },
			],
		};

	private static UiStack Small(UiState<JellyfinSessionsState> state)
		=> new()
		{
			Key = "small",
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.SpaceBetween,
			Children =
			[
				new UiStack
				{
					Key = "top",
					Direction = UiComponentDirections.Horizontal,
					Justify = UiComponentJustify.SpaceBetween,
					Children = [LeadArtwork(state, "artwork", _smallArtwork), Corner(state)],
				},
				new UiStack
				{
					Key = "text",
					Direction = UiComponentDirections.Vertical,
					Children =
					[
						LeadText(state, "title", row => row.Title, _title, primary: true),
						LeadText(state, "subtitle", row => row.Subtitle ?? row.Detail, _subtitle, primary: false),
					],
				},
			],
		};

	private static UiStack Medium(UiState<JellyfinSessionsState> state)
		=> SideBySide(state, "medium", _title, _subtitle);

	private static UiStack SideBySide(UiState<JellyfinSessionsState> state, string key, UiLength title, UiLength subtitle)
		=> new()
		{
			Key = key,
			Direction = UiComponentDirections.Horizontal,
			Gap = _rowGap,
			Children =
			[
				LeadArtwork(state, "artwork", UiLength.OfBasis(1)),
				new UiStack
				{
					Key = "details",
					Direction = UiComponentDirections.Vertical,
					Justify = UiComponentJustify.SpaceBetween,
					Fill = true,
					Children =
					[
						new UiStack
						{
							Key = "head",
							Direction = UiComponentDirections.Horizontal,
							Justify = UiComponentJustify.End,
							Children = [Corner(state)],
						},
						new UiStack
						{
							Key = "foot",
							Direction = UiComponentDirections.Vertical,
							Gap = _gap,
							Children =
							[
								new UiStack
								{
									Key = "text",
									Direction = UiComponentDirections.Vertical,
									Children =
									[
										LeadText(state, "title", row => row.Title, title, primary: true, lines: 2),
										LeadText(state, "subtitle", row => row.Subtitle ?? string.Empty, subtitle,
											primary: false),
										LeadText(state, "detail", row => row.Detail, _detail, primary: false),
									],
								},
								LeadProgress(state),
							],
						},
					],
				},
			],
		};

	private static UiStack Large(UiState<JellyfinSessionsState> state)
		=> new()
		{
			Key = "large",
			Direction = UiComponentDirections.Vertical,
			Children =
			[
				new UiWhen
				{
					Key = "heroGate",
					Condition = () => state.Value.Count == 1,
					Content = () => Hero(state),
				},
				new UiWhen
				{
					Key = "listGate",
					Condition = () => state.Value.Count > 1,
					Content = () => List(state, ListRows, withArtwork: true),
				},
			],
		};

	private static UiStack Hero(UiState<JellyfinSessionsState> state)
		=> SideBySide(state, "hero", _heroTitle, _title);

	private static UiStack List(UiState<JellyfinSessionsState> state, int rows, bool withArtwork)
		=> new()
		{
			Key = withArtwork ? "list" : "tallList",
			Direction = UiComponentDirections.Vertical,
			Gap = _rowGap,
			Children =
			[
				new UiStack
				{
					Key = "header",
					Direction = UiComponentDirections.Horizontal,
					Align = UiComponentAlignments.Center,
					Gap = _gap,
					Children =
					[
						Glyph(state),
						new UiTextRun
						{
							Key = "count",
							Text = UiText.FromLocalized(() => Strings.ActiveSessions(count: state.Value.Count)),
							Size = _headingText,
							Weight = UiComponentTextWeights.SemiBold,
							Role = UiComponentTextRoles.Secondary,
							MaxLines = 1,
							Fill = true,
						},
					],
				},
				new UiRepeat<JellyfinSessionRow>
				{
					Key = "rows",
					Items = UiValue.From<IReadOnlyList<JellyfinSessionRow>>(() => [.. state.Value.Rows.Take(rows)]),
					KeySelector = row => row.Key,
					Template = (row, _) => Row(row, withArtwork),
				},
			],
		};

	private static UiStack Row(JellyfinSessionRow row, bool withArtwork)
	{
		UiElement lead = withArtwork
			? Artwork("thumb", row.Artwork, row.IsAudio, row.IsPaused, _thumb)
			: new UiIcon
			{
				Key = "state",
				Icon = row.IsPaused ? UiIcons.Pause : UiIcons.Play,
				Size = _detail,
				MainSize = _detail,
				Role = UiComponentTextRoles.Secondary,
			};

		return new UiStack
		{
			Key = row.Key,
			Direction = UiComponentDirections.Horizontal,
			Align = UiComponentAlignments.Center,
			Gap = _rowGap,
			Children =
			[
				lead,
				new UiStack
				{
					Key = "text",
					Direction = UiComponentDirections.Vertical,
					Gap = UiLength.Capped(0.015, 2),
					Fill = true,
					Children =
					[
						Text("title", row.Title, _title, primary: true),
						Text("detail",
							withArtwork && !string.IsNullOrEmpty(row.Subtitle) ? $"{row.Subtitle} · {row.Detail}" : row.Detail,
							_detail,
							primary: false),
						Progress("progress", UiValue.Of(row.Progress)),
					],
				},
			],
		};
	}

	private static UiStack Notice(UiState<JellyfinSessionsState> state, string key, LocalizedText text)
		=> new()
		{
			Key = key,
			Direction = UiComponentDirections.Vertical,
			Align = UiComponentAlignments.Center,
			Justify = UiComponentJustify.Center,
			Gap = _gap,
			Fill = true,
			Children =
			[
				Glyph(state),
				new UiTextRun
				{
					Key = "text",
					Text = text,
					Size = _subtitle,
					Role = UiComponentTextRoles.Secondary,
					Align = UiComponentAlignments.Center,
					Wrap = true,
					MaxLines = 3,
				},
			],
		};

	private static UiStack Corner(UiState<JellyfinSessionsState> state)
		=> new()
		{
			Key = "corner",
			Direction = UiComponentDirections.Vertical,
			Align = UiComponentAlignments.End,
			Gap = _gap,
			Children =
			[
				Glyph(state),
				new UiTextRun
				{
					Key = "more",
					Text = UiText.From(() => state.Value.MoreBadge),
					Size = _badgeText,
					Weight = UiComponentTextWeights.SemiBold,
					Role = UiComponentTextRoles.Secondary,
					MaxLines = 1,
				},
			],
		};

	private static UiImage Glyph(UiState<JellyfinSessionsState> state)
		=> new()
		{
			Key = "glyph",
			Size = _glyph,
			MainSize = _glyph,
			Source = UiValue.Optional(() => state.Value.Glyph is { } glyph
				? UiValue.Of(glyph)
				: UiValue.None<UiResource>()),
		};

	private static UiStack LeadArtwork(UiState<JellyfinSessionsState> state, string key, UiLength size)
		=> new()
		{
			Key = key + "Box",
			MainSize = size,
			Children =
			[
				new UiWhen
				{
					Key = "imageGate",
					Condition = () => state.Value.Lead?.Artwork is not null,
					Content = () => new UiImage
					{
						Key = key,
						Size = size,
						Transition = UiComponentImageTransitions.Crossfade,
						Brightness = UiValue.From(() => state.Value.Lead?.IsPaused == true ? 0.6 : 1d),
						Source = UiValue.Optional(() => state.Value.Lead?.Artwork is { } artwork
							? UiValue.Of(artwork)
							: UiValue.None<UiResource>()),
					},
				},
				new UiWhen
				{
					Key = "placeholderGate",
					Condition = () => state.Value.Lead is { Artwork: null },
					Content = () => Placeholder(key + "Placeholder", state.Value.Lead?.IsAudio == true, size),
				},
			],
		};

	private static UiElement Artwork(string key, UiResource? artwork, bool isAudio, bool isPaused, UiLength size)
		=> artwork is { } resource
			? new UiImage
			{
				Key = key,
				Size = size,
				MainSize = size,
				Transition = UiComponentImageTransitions.Crossfade,
				Brightness = isPaused ? 0.6 : 1,
				Source = resource,
			}
			: Placeholder(key, isAudio, size);

	private static UiStack Placeholder(string key, bool isAudio, UiLength size)
		=> new()
		{
			Key = key,
			MainSize = size,
			Align = UiComponentAlignments.Center,
			Justify = UiComponentJustify.Center,
			Background = ArtworkPlaceholder,
			Children =
			[
				new UiIcon
				{
					Key = "icon",
					Icon = isAudio ? UiIcons.MusicNote : UiIcons.Play,
					Size = UiLength.OfBasis(size.Basis * 0.3),
					Role = UiComponentTextRoles.Secondary,
				},
			],
		};

	private static UiTextRun LeadText(
		UiState<JellyfinSessionsState> state,
		string key,
		Func<JellyfinSessionRow, string> select,
		UiLength size,
		bool primary,
		int lines = 1)
		=> new()
		{
			Key = key,
			Text = UiText.From(() => state.Value.Lead is { } lead ? select(lead) : string.Empty),
			Size = size,
			Weight = primary ? UiComponentTextWeights.SemiBold : UiValue.None<string>(),
			Role = primary ? UiComponentTextRoles.Primary : UiComponentTextRoles.Secondary,
			Wrap = lines > 1,
			MaxLines = lines,
		};

	private static UiTextRun Text(string key, string text, UiLength size, bool primary)
		=> new()
		{
			Key = key,
			Text = text,
			Size = size,
			Weight = primary ? UiComponentTextWeights.SemiBold : UiValue.None<string>(),
			Role = primary ? UiComponentTextRoles.Primary : UiComponentTextRoles.Secondary,
			MaxLines = 1,
		};

	private static UiRangeBar LeadProgress(UiState<JellyfinSessionsState> state)
		=> Progress("progress", UiValue.From(() => state.Value.Lead?.Progress ?? 0));

	private static UiRangeBar Progress(string key, UiValue<double> end)
		=> new()
		{
			Key = key,
			MainSize = ProgressThickness,
			Thickness = ProgressThickness,
			Start = 0d,
			End = end,
			StartColor = ProgressStart,
			EndColor = ProgressEnd,
		};
}
