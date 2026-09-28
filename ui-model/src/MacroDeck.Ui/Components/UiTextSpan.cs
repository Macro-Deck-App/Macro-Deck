using System.Text.Json.Serialization;
using MacroDeck.Ui.Model.Resources;

namespace MacroDeck.Ui.Components;

/// <summary>
/// One inline run of a <see cref="UiTextRun.Spans" /> flow: either text or an image, never both.
/// </summary>
/// <remarks>
/// A span's text is literal: it is drawn as given and never resolved as a localization reference, because
/// spans carry content such as a chat message rather than interface text. A reader draws a span with
/// neither <see cref="Text" /> nor <see cref="Image" /> as nothing.
/// </remarks>
public sealed record UiTextSpan
{
	/// <summary>The text of a text span.</summary>
	[JsonPropertyOrder(0)]
	public string? Text { get; init; }

	/// <summary>The image of an image span, drawn as a square one line high.</summary>
	[JsonPropertyOrder(1)]
	public UiResource? Image { get; init; }

	/// <summary>What an image span stands for, drawn in its place while the image is not available. Absent
	/// means the image is decorative and nothing is drawn in its place.</summary>
	[JsonPropertyOrder(2)]
	public string? Alt { get; init; }

	/// <summary>A literal colour for a text span, as <c>#rrggbb</c>. Absent means the run's own colour. A
	/// reader ignores any other spelling.</summary>
	[JsonPropertyOrder(3)]
	public string? Color { get; init; }

	/// <summary>The font weight of a text span - see <see cref="UiComponentTextWeights" />. Absent means the
	/// run's own weight.</summary>
	[JsonPropertyOrder(4)]
	public string? Weight { get; init; }

	/// <summary>A text span.</summary>
	public static UiTextSpan FromText(string text, string? color = null, string? weight = null)
	{
		ArgumentNullException.ThrowIfNull(text);

		return new UiTextSpan { Text = text, Color = color, Weight = weight };
	}

	/// <summary>An image span.</summary>
	/// <param name="image">The image.</param>
	/// <param name="alt">What the image stands for; null for a decorative image.</param>
	public static UiTextSpan FromImage(UiResource image, string? alt = null)
	{
		ArgumentNullException.ThrowIfNull(image);

		return new UiTextSpan { Image = image, Alt = alt };
	}
}
