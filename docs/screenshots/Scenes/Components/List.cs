using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

internal static partial class Scenes
{
	private sealed record CatalogItem(string Id, string Title, string Subtitle, string From, string To);

	private static IEnumerable<Scene> ListScenes()
	{
		IReadOnlyList<CatalogItem> items =
		[
			new("t1", "So What", "Miles Davis - Kind of Blue", "#2b6cee", "#1b2a4a"),
			new("t2", "Take Five", "The Dave Brubeck Quartet - Time Out", "#ff9500", "#8a2be2"),
			new("t3", "Naima", "John Coltrane - Giant Steps", "#34c759", "#0b3d2e"),
			new("t4", "Cantaloupe Island", "Herbie Hancock - Empyrean Isles", "#ff2d55", "#4a1030"),
		];

		yield return Dialog("list",
			new UiStack
			{
				Key = "dialog",
				Padding = UiSize.FromBasis(0.02),
				Children =
				[
					new UiList
					{
						Key = "results",
						Fill = true,
						Gap = UiSize.FromBasis(0.015),
						Events = [UiEventHandler.On(UiComponentEvents.Reveal, () => { })],
						Children =
						[
							new UiRepeat<CatalogItem>
							{
								Key = "rows",
								Items = UiValue.Of(items),
								KeySelector = item => item.Id,
								Template
									= (item, _) => TrackRow(item.Id, item.Title, item.Subtitle, item.From, item.To),
							},
						],
					},
				],
			},
			600,
			330);
	}
}
