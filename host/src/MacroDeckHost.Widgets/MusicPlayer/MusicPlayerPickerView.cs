using MacroDeck.Localization;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Components;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Widgets.MusicPlayer;

/// <summary>What the picker holds while it is open: what was found, and whether it could be.</summary>
internal sealed record MusicPlayerPickerState
{
	public required IReadOnlyList<MusicPlayerCatalogItem> Items { get; init; }

	/// <summary>The provider answered. False for unreachable, timed out, or rejected credentials.</summary>
	public required bool Available { get; init; }

	/// <summary>This instance cannot browse a library at all, so retrying will not help.</summary>
	public required bool Supported { get; init; }

	public static MusicPlayerPickerState Loading { get; } =
		new() { Items = [], Available = true, Supported = true };

	public static MusicPlayerPickerState Unsupported { get; } =
		new() { Items = [], Available = true, Supported = false };

	public static MusicPlayerPickerState Unavailable { get; } =
		new() { Items = [], Available = false, Supported = true };
}

/// <summary>
/// The Play Track / Play Playlist picker, as a Macro Deck UI dialog.
///
/// <para>
/// Every row answers with the item's own id and nothing else - see
/// <see cref="UiComponentContainer.Answer" />.
/// The client settles the dialog with it and the host plays what it named, so the value crossing the wire
/// is one the host issued and can look up rather than one it has to trust. That is also why the row's
/// press handler does nothing: it is there to <i>declare</i> the press, which is what makes the row
/// pressable at all, and the client never sends it because the answer settles the dialog first.
/// </para>
/// </summary>
internal static class MusicPlayerPickerView
{
	/// <summary>How many rows are in the tree before the user has scrolled at all.</summary>
	internal const int InitialWindow = 25;

	/// <summary>How many more each `reveal` adds. Enough that scrolling does not stutter at the seam.</summary>
	internal const int WindowGrowth = 25;

	public static UiElement Build(
		UiState<MusicPlayerPickerState> state,
		UiState<string> filter,
		UiState<int> window,
		UiState<IReadOnlyDictionary<string, UiResource>> artwork,
		MusicPlayerIconResources icons,
		IReadOnlyList<UiEventHandler> searchEvents,
		IReadOnlyList<UiEventHandler> listEvents)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(filter);
		ArgumentNullException.ThrowIfNull(window);
		ArgumentNullException.ThrowIfNull(artwork);
		ArgumentNullException.ThrowIfNull(icons);

		return new UiStack
		{
			Key = "picker",
			Direction = UiComponentDirections.Vertical,
			Gap = UiSize.FromBasis(0.03),
			Padding = UiSize.FromBasis(0.02),
			Children =
			[
				new UiTextField
				{
					Key = "search",
					Text = UiValue.From(() => filter.Value),
					Placeholder = AppStrings.Dialogs.ItemPicker.SearchPlaceholder(),
					Size = UiSize.FromBasis(0.045),
					Events = searchEvents,
				},
				Results(state, window, artwork, icons, listEvents),
			],
		};
	}

	private static UiList Results(
		UiState<MusicPlayerPickerState> state,
		UiState<int> window,
		UiState<IReadOnlyDictionary<string, UiResource>> artwork,
		MusicPlayerIconResources icons,
		IReadOnlyList<UiEventHandler> events)
		=> new()
		{
			Key = "results",
			Fill = true,
			Gap = UiSize.FromBasis(0.015),
			Events = events,
			Children =
			[
				Message("unsupported", () => !state.Value.Supported, AppStrings.Dialogs.ItemPicker.Unsupported()),
				Message("failed",
					() => state.Value.Supported && !state.Value.Available,
					AppStrings.Dialogs.ItemPicker.LoadFailed()),
				Message("empty",
					() => state.Value.Supported && state.Value.Available && state.Value.Items.Count == 0,
					AppStrings.Dialogs.ItemPicker.NoItemsFound()),
				new UiRepeat<MusicPlayerCatalogItem>
				{
					Key = "rows",
					// Windowed rather than whole: the list asks for more as the user reaches its end, and a
					// library of several hundred would otherwise put every row on the wire to show ten.
					Items
						= UiValue.From<IReadOnlyList<MusicPlayerCatalogItem>>(() => Window(state.Value, window.Value)),
					KeySelector = item => item.Id,
					Template = (item, _) => Row(item, artwork, icons),
				},
			],
		};

	private static IReadOnlyList<MusicPlayerCatalogItem> Window(MusicPlayerPickerState state, int window)
	{
		if (state.Items.Count <= window)
		{
			return state.Items;
		}

		var rows = new List<MusicPlayerCatalogItem>(window);
		for (var index = 0; index < window; index++)
		{
			rows.Add(state.Items[index]);
		}

		return rows;
	}

	private static UiWhen Message(string key, Func<bool> when, LocalizedText text)
		=> new()
		{
			Key = $"{key}When",
			Condition = when,
			Content = () => new UiStack
			{
				Key = key,
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Padding = UiSize.FromBasis(0.06),
				Children =
				[
					new UiTextRun
					{
						Key = "text",
						Text = text,
						Size = UiSize.FromBasis(0.04),
						Role = UiComponentTextRoles.Secondary,
						Align = UiComponentAlignments.Center,
						Wrap = true,
					},
				],
			},
		};

	private static UiStack Row(
			MusicPlayerCatalogItem item,
			UiState<IReadOnlyDictionary<string, UiResource>> artwork,
			MusicPlayerIconResources icons)
		// A stack rather than a button: both press the same way - the affordance follows the declared
		// events - but a button's absent background is the accent colour, which is right for a deck tile
		// and wrong for every row of a list.
		=> new()
		{
			// The item's own id, not its position: rows are patched in place as the window grows and the
			// filter narrows, and a key that moved with the position would re-key everything below a change.
			Key = item.Id,
			Answer = UiValue.Of(item.Id),
			Events = [UiEventHandler.On(UiComponentEvents.Press, Answered)],
			Direction = UiComponentDirections.Horizontal,
			Align = UiComponentAlignments.Center,
			Gap = UiSize.FromBasis(0.025),
			Padding = UiSize.FromBasis(0.02),
			Children =
			[
				// Always drawn, and the icon stands in until - or instead of - a cover: rows that
				// differ in height as their covers arrive make the whole list twitch while it loads,
				// and a library where half the items have no artwork would never settle at all.
				new UiImage
				{
					Key = "art",
					Source = UiValue.From(() => Cover(item, artwork.Value) ?? Placeholder(item, icons)),
					Size = UiSize.FromBasis(0.09),
				},
				new UiStack
				{
					Key = "labels",
					Fill = true,
					Direction = UiComponentDirections.Vertical,
					Justify = UiComponentJustify.Center,
					Gap = UiSize.FromBasis(0.004),
					Children =
					[
						new UiTextRun
						{
							Key = "title",
							Text = item.Title,
							Size = UiSize.FromBasis(0.038),
							Weight = UiComponentTextWeights.Medium,
						},
						new UiWhen
						{
							Key = "subtitleWhen",
							Condition = () => !string.IsNullOrEmpty(item.Subtitle),
							Content = () => new UiTextRun
							{
								Key = "subtitle",
								Text = item.Subtitle,
								Size = UiSize.FromBasis(0.032),
								Role = UiComponentTextRoles.Secondary,
							},
						},
					],
				},
			],
		};

	private static UiResource? Cover(
		MusicPlayerCatalogItem item,
		IReadOnlyDictionary<string, UiResource> artwork)
		=> item.ArtworkId is { Length: > 0 } artworkId && artwork.TryGetValue(artworkId, out var resource)
			? resource
			: null;

	/// <summary>What a row shows before its cover arrives, and instead of one that never does.</summary>
	private static UiResource Placeholder(MusicPlayerCatalogItem item, MusicPlayerIconResources icons)
		=> item.Kind == MusicPlayerCatalogItemKind.Playlist ? icons.Disc : icons.MusicNote;

	/// <summary>
	/// Declared so the row is pressable, and deliberately empty: the answer settles the dialog on the
	/// client, which never sends the press at all. A node that declared nothing would not be pressable.
	/// </summary>
	private static void Answered()
	{
	}
}
