using MacroDeck.Plugin.Protocol.Limits;
using MacroDeckHost.Application.Widgets.Icons;

namespace MacroDeckHost.Application.Widgets;

public enum WidgetIconRenditionStatus
{
	Rendered,
	NotFound,
	TooLarge
}

public sealed record WidgetIconRendition(WidgetIconRenditionStatus Status, byte[]? Content = null, string? MediaType = null);

public static class WidgetIconRenditions
{
	// A deck tile never needs a sharper icon than this, and the retired Slider component used the same
	// rendition size. The further candidates only come into play when the first is too large to register.
	private static readonly (int Size, bool StaticFrame)[] _candidates =
	[
		(256, false),
		(128, false),
		(128, true)
	];

	public static async Task<WidgetIconRendition> ProduceAsync(IWidgetIconSource source,
		string reference,
		CancellationToken cancellationToken)
	{
		// Never WebP: one rendition goes to every client and Safari before 14 draws WebP blank. An animated
		// GIF over the UI resource limit steps down in size and finally to its first frame.
		foreach (var (size, staticFrame) in _candidates)
		{
			var image = await source
				.GetImageAsync(reference, size, acceptWebp: false, staticFrame, cancellationToken)
				.ConfigureAwait(false);

			if (image is null)
			{
				return new WidgetIconRendition(WidgetIconRenditionStatus.NotFound);
			}

			byte[] content;
			try
			{
				using var memory = new MemoryStream();
				await image.Content.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
				content = memory.ToArray();
			}
			finally
			{
				await image.Content.DisposeAsync().ConfigureAwait(false);
			}

			if (content.Length <= ProtocolLimits.MaxUiResourceBytes)
			{
				return new WidgetIconRendition(WidgetIconRenditionStatus.Rendered, content, image.MediaType);
			}
		}

		return new WidgetIconRendition(WidgetIconRenditionStatus.TooLarge);
	}
}
