using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

internal static partial class Scenes
{
	private static UiGradient Linear(double angle, string from, string to)
		=> UiGradient.Linear(angle, Stop(0, from), Stop(1, to));

	private static UiGradient Radial(string from, string to)
		=> UiGradient.Radial(0.5, 0.5, Stop(0, from), Stop(1, to));

	private static UiGradientStop Stop(double offset, string color) => new() { Offset = offset, Color = color };

	private static UiMask FadeDown()
		=> UiMask.Linear(180, new UiMaskStop { Offset = 0.4, Opacity = 1 }, new UiMaskStop { Offset = 1, Opacity = 0 });

	private static UiSize Hairline(double units) => UiSize.Capped(units / UiLength.Cell, units);

	private static UiButton Art(string key) => new()
	{
		Key = key,
		Source = Cover("modifier-cover", "#2b6cee", "#7a3cf0"),
		Fit = UiComponentImageFits.Cover,
		Corner = UiComponentButtonCorners.Tile,
	};

	private static UiStack Swatch(Func<UiElement, UiModifier> modifier)
		=> Centered("tile", modifier(new UiImage { Key = "swatch", Source = Icon("music-note"), Size = 0.6 }));

	private static UiModifier Framed(string key, UiFrame frame) => new()
	{
		Key = key,
		Frame = frame,
		Child = Art(key + "Art"),
	};

	private static UiModifier Framed(string key, UiFrame frame, string clip) => Framed(key, frame) with { Clip = clip };

	private static IEnumerable<Scene> ModifierScenes()
	{
		yield return Tile("modifier-background",
			new UiModifier
			{
				Key = "mod1", Background = "#2b6cee",
				Child = Centered("tile", new UiImage { Key = "icon", Source = Icon("music-note"), Size = 0.4 })
			});

		yield return Tile("modifier-linear",
			new UiModifier
			{
				Key = "mod2", Background = Linear(135, "#ff2d55", "#ff9500"),
				Child = Centered("tile", new UiImage { Key = "icon", Source = Icon("heart"), Size = 0.4 })
			});

		yield return Tile("modifier-radial",
			new UiModifier
			{
				Key = "mod3", Background = Radial("#34c759", "#0b3d1a"),
				Child = Centered("tile", new UiImage { Key = "icon", Source = Icon("power"), Size = 0.4 })
			});

		yield return Tile("modifier-radius",
			Swatch(child => new UiModifier { Key = "mod4", Background = "#2b6cee", Radius = 0.12, Child = child }));

		foreach (var line in new[]
			{ UiComponentBorderLines.Solid, UiComponentBorderLines.Dashed, UiComponentBorderLines.Dotted })
		{
			yield return Tile("modifier-border-" + line,
				Swatch(child => new UiModifier
				{
					Key = "border",
					Child = child,
					Background = "#1c1c1e",
					Radius = 0.1,
					BorderWidth = Hairline(3),
					BorderColor = "#ff9500",
					BorderLine = line,
				}));
		}

		yield return Tile("modifier-border-button",
			new UiStack
			{
				Key = "tile",
				Padding = TilePadding,
				Children =
				[
					new UiModifier
					{
						Key = "mod5",
						BorderWidth = Hairline(4),
						BorderColor = "#ffffff",
						Child = Art("cover") with { Fill = true, Corner = default },
					},
				],
			});

		yield return Tile("modifier-disabled",
			new UiModifier
			{
				Key = "disabled",
				Disabled = true,
				Child = new UiButton
				{
					Key = "mute",
					Justify = UiComponentJustify.Center,
					Background = "#c62f2f",
					Source = Svg("mic-off", MicOff),
					Fit = UiComponentImageFits.Cover,
					Events = [UiEventHandler.On(UiComponentEvents.Press, () => { })],
					Children =
					[
						new UiTextRun
							{ Key = "label", Text = "Mute", Size = 0.14, Align = UiComponentAlignments.Center }
					],
				},
			});

		yield return Tile("modifier-padding",
			new UiModifier
			{
				Key = "card",
				Background = Linear(135, "#2b6cee", "#7a3cf0"),
				Padding = 0.2,
				Child = new UiImage { Key = "icon", Source = Icon("music-note") },
				Fallback = new UiImage { Key = "iconPlain", Source = Icon("music-note") },
			});

		yield return Tile("modifier-opacity",
			new UiModifier { Key = "faded", Opacity = 0.4, Child = Art("cover") });

		yield return Tile("modifier-clip-bounds",
			Centered("tile",
				Framed("clipped",
						new UiFrame { Width = UiLength.OfBasis(0.7), Height = UiLength.OfBasis(0.7) },
						UiComponentClips.Bounds) with
					{
						Radius = 0.2
					}));

		yield return Tile("modifier-clip-circle",
			Centered("tile",
				Framed("clipped",
					new UiFrame { Width = UiLength.OfBasis(0.7), Height = UiLength.OfBasis(0.7) },
					UiComponentClips.Circle)));

		yield return Tile("modifier-clip-capsule",
			Centered("tile",
				Framed("clipped",
					new UiFrame { Width = UiLength.OfBasis(0.9), Height = UiLength.OfBasis(0.45) },
					UiComponentClips.Capsule)));

		yield return Tile("modifier-mask",
			new UiModifier { Key = "masked", Mask = FadeDown(), Child = Art("cover") });

		yield return Tile("modifier-frame-fixed",
			Centered("tile",
				Framed("framed", new UiFrame { Width = UiLength.OfBasis(0.5), Height = UiLength.OfBasis(0.3) })));

		yield return Tile("modifier-frame-minmax",
			Centered("tile",
				Framed("framed",
						new UiFrame
						{
							MinWidth = UiLength.OfBasis(0.3), MaxWidth = UiLength.OfBasis(0.6),
							MinHeight = UiLength.OfBasis(0.25), MaxHeight = UiLength.OfBasis(0.4)
						}) with
					{
						Fill = true
					}),
			2);

		yield return Tile("modifier-frame-aspect",
			Centered("tile", Framed("framed", new UiFrame { AspectRatio = 16d / 9 }) with { Fill = true }),
			2);
	}
}
