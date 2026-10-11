using MacroDeck.Localization;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Steam;

namespace MacroDeckHost.Integrations.Steam.Widgets;

internal static class SteamPlayingView
{
	public const string Root = "steamPlaying";

	private const string ArtworkPlaceholder = "#1b2838";

	private static readonly UiLength _title = UiLength.Capped(0.12, 16);
	private static readonly UiLength _detail = UiLength.Capped(0.09, 12);
	private static readonly UiLength _notice = UiLength.Capped(0.09, 12);
	private static readonly UiLength _glyph = UiLength.Capped(0.2, 28);
	private static readonly UiLength _smallArtwork = UiLength.Capped(0.46, 56);
	private static readonly UiLength _gap = UiLength.Capped(0.04, 5);
	private static readonly UiLength _rowGap = UiLength.Capped(0.06, 8);

	public static UiElement Build(
		UiState<SteamPlayingState> state,
		int cornerRadius = SteamSurfaces.DefaultCornerRadius,
		string? backgroundColor = null)
	{
		ArgumentNullException.ThrowIfNull(state);

		return new UiStack
		{
			Key = Root,
			Direction = UiComponentDirections.Vertical,
			Padding = SteamSurfaces.SafeArea(cornerRadius),
			Background = backgroundColor is { } background ? UiValue.Of(background) : UiValue.None<string>(),
			Children =
			[
				Gate(state, "unavailable", SteamWidgetStatus.Unavailable, Strings.Unavailable()),
				Gate(state, "notFound", SteamWidgetStatus.NotFound, Strings.NotFound()),
				Gate(state, "accountUnavailable", SteamWidgetStatus.AccountUnavailable, Strings.AccountUnavailable()),
				Gate(state, "idle", SteamWidgetStatus.Idle, Strings.NotPlaying()),
				new UiWhen
				{
					Key = "readyGate",
					Condition = () => state.Value.Status == SteamWidgetStatus.Ready,
					Content = () => Layout(state),
				},
			],
		};
	}

	private static UiWhen Gate(UiState<SteamPlayingState> state, string key, SteamWidgetStatus status,
		LocalizedText text)
		=> new()
		{
			Key = key + "Gate",
			Condition = () => state.Value.Status == status,
			Content = () => Notice(state, key, text),
		};

	private static UiStack Notice(UiState<SteamPlayingState> state, string key, LocalizedText text)
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
				new UiImage
				{
					Key = "glyph",
					Size = _glyph,
					MainSize = _glyph,
					Source = UiValue.Optional(() => state.Value.Glyph is { } glyph
						? UiValue.Of(glyph)
						: UiValue.None<UiResource>()),
				},
				new UiTextRun
				{
					Key = "text",
					Text = text,
					Size = _notice,
					Role = UiComponentTextRoles.Secondary,
					Align = UiComponentAlignments.Center,
					Wrap = true,
					MaxLines = 3,
				},
			],
		};

	private static UiResponsive Layout(UiState<SteamPlayingState> state)
		=> new()
		{
			Key = "layout",
			Fill = true,
			Default = Small(state),
			Variants = [new UiResponsiveVariant { MinWidth = 1.5, Content = SideBySide(state) }],
		};

	private static UiStack Small(UiState<SteamPlayingState> state)
		=> new()
		{
			Key = "small",
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.SpaceBetween,
			Children = [Artwork(state, "artwork", _smallArtwork), Texts(state, "text", lines: 2)],
		};

	private static UiStack SideBySide(UiState<SteamPlayingState> state)
		=> new()
		{
			Key = "wide",
			Direction = UiComponentDirections.Horizontal,
			Gap = _rowGap,
			Children =
			[
				Artwork(state, "artwork", UiLength.OfBasis(1)),
				new UiStack
				{
					Key = "details",
					Direction = UiComponentDirections.Vertical,
					Justify = UiComponentJustify.End,
					Fill = true,
					Children = [Texts(state, "text", lines: 3)],
				},
			],
		};

	private static UiStack Texts(UiState<SteamPlayingState> state, string key, int lines)
		=> new()
		{
			Key = key,
			Direction = UiComponentDirections.Vertical,
			Children =
			[
				new UiTextRun
				{
					Key = "title",
					Text = UiText.From(() => state.Value.Title),
					Size = _title,
					Weight = UiComponentTextWeights.SemiBold,
					Role = UiComponentTextRoles.Primary,
					Wrap = lines > 1,
					MaxLines = lines,
				},
				new UiWhen
				{
					Key = "elapsedGate",
					Condition = () => state.Value.Elapsed.Length > 0,
					Content = () => new UiTextRun
					{
						Key = "elapsed",
						Text = UiText.FromLocalized(() => Strings.PlayingFor(time: state.Value.Elapsed)),
						Size = _detail,
						Role = UiComponentTextRoles.Secondary,
						MaxLines = 1,
					},
				},
			],
		};

	private static UiStack Artwork(UiState<SteamPlayingState> state, string key, UiLength size)
		=> new()
		{
			Key = key + "Box",
			MainSize = size,
			Children =
			[
				new UiWhen
				{
					Key = "imageGate",
					Condition = () => state.Value.Cover is not null,
					Content = () => new UiImage
					{
						Key = key,
						Size = size,
						Transition = UiComponentImageTransitions.Crossfade,
						Source = UiValue.Optional(() => state.Value.Cover is { } cover
							? UiValue.Of(cover)
							: UiValue.None<UiResource>()),
					},
				},
				new UiWhen
				{
					Key = "placeholderGate",
					Condition = () => state.Value.Cover is null,
					Content = () => new UiStack
					{
						Key = key + "Placeholder",
						MainSize = size,
						Align = UiComponentAlignments.Center,
						Justify = UiComponentJustify.Center,
						Background = ArtworkPlaceholder,
						Children =
						[
							new UiImage
							{
								Key = "glyph",
								Size = UiLength.OfBasis(size.Basis * 0.4),
								Source = UiValue.Optional(() => state.Value.Glyph is { } glyph
									? UiValue.Of(glyph)
									: UiValue.None<UiResource>()),
							},
						],
					},
				},
			],
		};
}
