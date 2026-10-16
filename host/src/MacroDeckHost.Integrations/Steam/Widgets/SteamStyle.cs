using MacroDeck.Localization;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Integrations.Steam.State;

namespace MacroDeckHost.Integrations.Steam.Widgets;

internal enum SteamTone
{
	Primary,

	Secondary,

	Muted
}

internal sealed record SteamInk(bool OnFace)
{
	public static SteamInk For(string? backgroundColor)
	{
		return new SteamInk(backgroundColor is null);
	}

	public UiValue<string> Color(SteamTone tone)
	{
		return OnFace
			? UiValue.Of(tone switch
			{
				SteamTone.Primary => SteamStyle.TextPrimary,
				SteamTone.Secondary => SteamStyle.TextSecondary,
				_ => SteamStyle.TextMuted
			})
			: UiValue.None<string>();
	}

	public static string Role(SteamTone tone)
	{
		return tone switch
		{
			SteamTone.Primary => UiComponentTextRoles.Primary,
			SteamTone.Secondary => UiComponentTextRoles.Secondary,
			_ => UiComponentTextRoles.Muted
		};
	}
}

internal static class SteamStyle
{
	public const string TextPrimary = "#ffffff";
	public const string TextSecondary = "#ffffffbf";
	public const string TextMuted = "#ffffff80";
	public const string InGame = "#a4d007";
	public const string Online = "#57cbde";
	public const string Away = "#5f8ea3";
	public const string Offline = "#7a8592";
	public const string Failure = "#ff6961";
	public const string Placeholder = "#22303f";

	public static readonly UiLength Eyebrow = UiLength.Capped(0.075, 10);
	public static readonly UiLength EyebrowGlyph = UiLength.Capped(0.1, 13);
	public static readonly UiLength Title = UiLength.Capped(0.13, 17);
	public static readonly UiLength Body = UiLength.Capped(0.095, 12.5);
	public static readonly UiLength Caption = UiLength.Capped(0.08, 10.5);
	public static readonly UiLength Numeral = UiLength.Capped(0.34, 42);
	public static readonly UiLength Gap = UiLength.Capped(0.04, 5);
	public static readonly UiLength SmallGap = UiLength.Capped(0.025, 3);
	public static readonly UiLength ArtworkRadius = UiLength.Capped(0.07, 9);

	private static readonly UiGradient _face = UiGradient.Linear(160,
		new UiGradientStop { Offset = 0, Color = "#2a3f5a" },
		new UiGradientStop { Offset = 0.55, Color = "#18222f" },
		new UiGradientStop { Offset = 1, Color = "#0d1219" });

	private static readonly UiGradient _scrim = UiGradient.Linear(180,
		new UiGradientStop { Offset = 0, Color = "#00000000" },
		new UiGradientStop { Offset = 0.45, Color = "#00000000" },
		new UiGradientStop { Offset = 1, Color = "#000000d9" });

	private static readonly UiGradient _backdropScrim = UiGradient.Linear(90,
		new UiGradientStop { Offset = 0, Color = "#0d1219e6" },
		new UiGradientStop { Offset = 1, Color = "#0d121980" });

	public static UiElement Root(string key, string? backgroundColor, UiElement content)
	{
		var root = new UiStack
		{
			Key = key,
			Direction = UiComponentDirections.Vertical,
			Background = backgroundColor is { } background ? UiValue.Of(background) : UiValue.None<string>(),
			Children = [content]
		};

		return backgroundColor is null ? new UiModifier { Key = key + "Face", Background = _face, Child = root } : root;
	}

	public static UiStack Padded(string key, int cornerRadius, params IReadOnlyList<UiElement> children)
	{
		return new UiStack
		{
			Key = key,
			Direction = UiComponentDirections.Vertical,
			Padding = SteamSurfaces.SafeArea(cornerRadius),
			Gap = Gap,
			Children = children
		};
	}

	public static UiModifier Scrim(string key)
	{
		return new UiModifier { Key = key, Background = _scrim, Child = new UiStack { Key = "fill", Fill = true } };
	}

	public static UiModifier BackdropScrim(string key)
	{
		return new UiModifier
			{ Key = key, Background = _backdropScrim, Child = new UiStack { Key = "fill", Fill = true } };
	}

	public static UiStack EyebrowRow(
		string key,
		LocalizedText text,
		Func<UiResource?> glyph,
		SteamInk ink,
		string? accent = null)
	{
		return new UiStack
		{
			Key = key,
			Direction = UiComponentDirections.Horizontal,
			Align = UiComponentAlignments.Center,
			Gap = SmallGap,
			Children =
			[
				new UiImage
				{
					Key = "glyph",
					Size = EyebrowGlyph,
					MainSize = EyebrowGlyph,
					Source = UiValue.Optional(() => glyph() is { } resource
						? UiValue.Of(resource)
						: UiValue.None<UiResource>())
				},
				new UiTextRun
				{
					Key = "label",
					Text = text,
					Size = Eyebrow,
					Weight = UiComponentTextWeights.Bold,
					Role = SteamInk.Role(SteamTone.Secondary),
					Color = accent is { } color ? UiValue.Of(color) : ink.Color(SteamTone.Secondary),
					MaxLines = 1
				}
			]
		};
	}

	public static UiStack StatusLabel(string key, UiText text, UiValue<string> color, UiLength? size = null)
	{
		return new UiStack
		{
			Key = key,
			Direction = UiComponentDirections.Horizontal,
			Align = UiComponentAlignments.Center,
			Gap = SmallGap,
			Children =
			[
				Dot("dot", UiLength.Capped(0.05, 6), color),
				new UiTextRun
				{
					Key = "label",
					Text = text,
					Size = size ?? Eyebrow,
					Weight = UiComponentTextWeights.Bold,
					Color = color,
					MaxLines = 1
				}
			]
		};
	}

	public static UiTextRun Text(
		string key,
		UiText text,
		UiLength size,
		SteamInk ink,
		SteamTone tone = SteamTone.Primary,
		string weight = UiComponentTextWeights.Regular,
		int maxLines = 1,
		string align = UiComponentAlignments.Start)
	{
		return new UiTextRun
		{
			Key = key,
			Text = text,
			Size = size,
			Weight = weight,
			Role = SteamInk.Role(tone),
			Color = ink.Color(tone),
			Align = align,
			Wrap = maxLines > 1,
			MaxLines = maxLines
		};
	}

	public static UiModifier Rounded(string key, UiLength radius, UiElement child, UiLength? mainSize = null)
	{
		var modifier = new UiModifier
		{
			Key = key,
			Radius = radius,
			Clip = UiComponentClips.Bounds,
			Child = child
		};

		return mainSize is { } size ? modifier with { MainSize = size } : modifier;
	}

	public static UiModifier Dot(string key, UiLength size, UiValue<string> color)
	{
		return new UiModifier
		{
			Key = key,
			Frame = new UiFrame { Width = size, Height = size },
			Child = new UiShape { Key = "shape", Shape = UiComponentShapes.Circle, Color = color }
		};
	}

	public static UiStack GlyphTile(string key, UiLength size, Func<UiResource?> glyph)
	{
		return new UiStack
		{
			Key = key,
			Fill = true,
			Align = UiComponentAlignments.Center,
			Justify = UiComponentJustify.Center,
			Background = Placeholder,
			Children =
			[
				new UiImage
				{
					Key = "glyph",
					Size = size,
					Opacity = 0.55,
					Source = UiValue.Optional(() => glyph() is { } resource
						? UiValue.Of(resource)
						: UiValue.None<UiResource>())
				}
			]
		};
	}

	public static UiStack Notice(
		string key,
		LocalizedText text,
		Func<UiResource?> glyph,
		SteamInk ink,
		int cornerRadius)
	{
		return new UiStack
		{
			Key = key,
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.SpaceBetween,
			Fill = true,
			Padding = SteamSurfaces.SafeArea(cornerRadius),
			Children =
			[
				Badge("badge", glyph),
				Text("text", text, Body, ink, SteamTone.Secondary, UiComponentTextWeights.Medium, 3)
			]
		};
	}

	// A stack hands every child its whole cross extent, so the glyph sits in a row to stay at the leading edge.
	public static UiStack Badge(string key, Func<UiResource?> glyph)
	{
		return new UiStack
		{
			Key = key,
			Direction = UiComponentDirections.Horizontal,
			Children =
			[
				new UiImage
				{
					Key = "glyph",
					Size = UiLength.Capped(0.2, 26),
					MainSize = UiLength.Capped(0.2, 26),
					Source = UiValue.Optional(() => glyph() is { } resource
						? UiValue.Of(resource)
						: UiValue.None<UiResource>())
				}
			]
		};
	}

	public static string PersonaColor(SteamFriend friend)
	{
		return friend.IsInGame ? InGame :
			!friend.IsOnline ? Offline :
			friend.PersonaState is SteamPersona.Away or SteamPersona.Snooze or SteamPersona.Busy ? Away : Online;
	}
}
