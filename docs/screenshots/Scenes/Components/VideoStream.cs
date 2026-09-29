using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.References;

internal static partial class Scenes
{
	private static IEnumerable<Scene> VideoStreamScenes()
	{
		// render.html plays the resource registered as "stream:<id>" as the stream's MJPEG frame.
		Svg("stream:program",
			"""<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="900" viewBox="0 0 160 90"><defs><linearGradient id="s" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#3a7bd5"/><stop offset="1" stop-color="#9ad0ec"/></linearGradient></defs><rect width="160" height="90" fill="url(#s)"/><circle cx="124" cy="22" r="9" fill="#ffe08a"/><path d="M0 70 L38 40 L62 60 L92 30 L160 72 L160 90 L0 90 Z" fill="#2f5d50"/><path d="M0 80 L50 62 L100 78 L160 66 L160 90 L0 90 Z" fill="#1f3f36"/><rect x="6" y="6" width="22" height="9" rx="2" fill="#e5484d"/><text x="17" y="13" font-family="sans-serif" font-size="6" font-weight="700" fill="#ffffff" text-anchor="middle">LIVE</text></svg>""");

		var program = UiValue.Of(new UiVideoStreamReference { Provider = "com.example.obs::studio", Id = "program" });

		yield return Tile("video-stream",
			new UiStack
			{
				Key = "tile",
				Direction = UiComponentDirections.Horizontal,
				Gap = 0.04,
				Padding = TilePadding,
				Children =
				[
					new UiVideoStream { Key = "contained", Stream = program, Fill = true },
					new UiVideoStream { Key = "covered", Stream = program, Fit = UiComponentImageFits.Cover, Fill = true },
				],
			},
			2);
	}
}
