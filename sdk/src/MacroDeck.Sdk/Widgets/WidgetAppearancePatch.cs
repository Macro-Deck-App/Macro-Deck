namespace MacroDeck.Sdk.Widgets;

/// <summary>
/// A partial appearance change. Every property is nullable and null means "leave as it is", so an
/// action that changes one thing sends one property and the host never has to know what the other
/// twelve currently are.
///
/// The union of what the widget types can show. Which properties a given type actually renders is
/// the host's business (see the widget appearance service); an action declares its intent and a
/// property the target type has no notion of is dropped rather than stored where nothing reads it.
/// </summary>
public sealed record WidgetAppearancePatch
{
	/// <summary>Label text. May carry a Liquid template, which the host resolves as it does a stored one.</summary>
	public string? Label { get; init; }

	public string? BackgroundColor { get; init; }

	public string? LabelColor { get; init; }

	/// <summary>Icon id, or an empty string to remove the icon.</summary>
	public string? IconId { get; init; }

	/// <summary>
	/// How the icon is fitted into the widget face: "contain" or "cover". Both keep the image's
	/// aspect ratio; the framing below is applied on top and never alters the stored image.
	/// </summary>
	public string? IconFit { get; init; }

	/// <summary>Icon zoom in percent, 100 being the plain fit.</summary>
	public double? IconZoom { get; init; }

	/// <summary>Icon shift along the widget's width, in percent of it; 0 is centered.</summary>
	public double? IconOffsetX { get; init; }

	/// <summary>Icon shift along the widget's height, in percent of it; 0 is centered.</summary>
	public double? IconOffsetY { get; init; }

	/// <summary>Icon opacity in percent, so an icon can sit behind the label as a background.</summary>
	public double? IconOpacity { get; init; }

	/// <summary>
	/// Stable id of the label's font face, in the host's face-catalog id format. A style that does not
	/// exist for the chosen family is simply not offered - this never carries a family name alone, so
	/// there is nothing here that could silently resolve to the wrong weight or style.
	/// </summary>
	public string? FontFaceId { get; init; }

	/// <summary>Font size as a percentage of the widget's smaller dimension, matching stored data.</summary>
	public double? FontSize { get; init; }

	/// <summary>"left", "center" or "right".</summary>
	public string? TextAlign { get; init; }

	/// <summary>"top", "center" or "bottom".</summary>
	public string? LabelPosition { get; init; }

	/// <summary>A <c>WidgetBorderStyle</c> value; "off" removes the border.</summary>
	public string? BorderStyle { get; init; }

	public string? BorderColor { get; init; }

	public bool IsEmpty => Label is null &&
		BackgroundColor is null &&
		LabelColor is null &&
		IconId is null &&
		IconFit is null &&
		IconZoom is null &&
		IconOffsetX is null &&
		IconOffsetY is null &&
		IconOpacity is null &&
		FontFaceId is null &&
		FontSize is null &&
		TextAlign is null &&
		LabelPosition is null &&
		BorderStyle is null &&
		BorderColor is null;
}
